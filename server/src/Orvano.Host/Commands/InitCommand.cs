using System.Buffers.Text;
using System.CommandLine;
using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Orvano.Infrastructure.RootKeys;

namespace Orvano.Host.Commands;

/// <summary>
/// <c>orvano init</c>: run once before the first <c>compose up</c>, as the operator's own user. Writes the install bundle
/// (<c>compose.yaml</c>, <c>Caddyfile</c>, <c>postgres-init/</c>) from the image's <c>/app/install/</c>, then a
/// <c>.env</c> with generated database passwords and the pinned version, then <c>root.key</c>, into a 0700 folder, and
/// refuses when any of them exists (spec 0007 INS-3, INS-5, spec 0016 BND-1 to BND-4, PIN-2, LCL-2, LCL-3, spec 0006
/// SCR-2). It needs no configuration, database, or root key.
/// </summary>
internal static partial class InitCommand
{
    /// <summary>The image repository <c>compose.yaml</c> runs when <c>.env</c> names none (spec 0016 BND-4).</summary>
    public const string DefaultImage = "ghcr.io/orvanohq/orvano";

    /// <summary>The image's own version, set by the <c>Dockerfile</c> (spec 0016 PIN-1); <c>dev</c> outside an image.</summary>
    public const string ImageVersionVariable = "ORVANO_IMAGE_VERSION";

    // Spec 0007 INS-5: root.key 0644 and .env 0600, inside a 0700 folder. Spec 0016 BND-2: compose.yaml and Caddyfile
    // 0644, and postgres-init/ 0755, so the postgres container reads the script through its own mount. The script is
    // 0755, as in the repository: the postgres entrypoint runs a script it sees as executable, and Docker Desktop's file
    // sharing reports a 0644 file as executable, so the run then fails with "Permission denied".
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode Readable = OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode FolderMode = OwnerOnly | UnixFileMode.UserExecute;
    private const UnixFileMode Executable = Readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
    private const UnixFileMode AllPermissions = (UnixFileMode)0b111_111_111;

    private const string PostgresInit = "postgres-init";
    private const string RolesScript = "10-orvano-roles.sh";

    // The copied bundle, in the order init writes it (BND-2): relative paths under /app/install/ and the install folder.
    private static readonly string[] BundleFiles = ["compose.yaml", "Caddyfile", Path.Combine(PostgresInit, RolesScript)];

    private const string UserHint =
        "Run init as yourself: docker run --user \"$(id -u):$(id -g)\" ... "
        + "(rootless Docker: --user 0:0, which is you on the host; rootless Podman: add --userns=keep-id to the --user form).";

    private const string FolderHint =
        "Use a folder you own on a local disk (chown it to yourself, or make a new one), not FAT, NFS, or a shared folder.";

    private const string LocalHint = "For a laptop or a test machine, use --local instead: it serves http://localhost.";

    public static Command Create()
    {
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = "The install folder to write the bundle, root.key, and .env into.",
            Required = true,
        };
        var domain = new Option<string?>("--domain")
        {
            Description = "Server mode: the domain Orvano is served on with HTTPS, for example orvano.example.com.",
        };
        var local = new Option<bool>("--local")
        {
            Description = "Local mode: plain HTTP on http://localhost, on this machine only.",
        };
        // A string, parsed below: System.CommandLine has no parser for Uri and would crash instead of explaining.
        var publicUrl = new Option<string?>("--public-url")
        {
            Description = "Server mode: the https URL clients use to reach Orvano. Defaults to https://<domain>.",
        };
        var httpPort = new Option<string?>("--http-port")
        {
            Description = "Local mode: the port on 127.0.0.1 to serve on. Defaults to 80.",
        };
        var image = new Option<string?>("--image")
        {
            Description = $"The image repository the install runs. Defaults to {DefaultImage}.",
        };

        var command = new Command("init", "Write the install bundle, the root key, and .env for a new install.")
        {
            output, domain, local, publicUrl, httpPort, image,
        };
        command.SetAction(parseResult => Run(new InitRequest(
            parseResult.GetRequiredValue(output),
            parseResult.GetValue(domain),
            parseResult.GetValue(local),
            parseResult.GetValue(publicUrl),
            parseResult.GetValue(httpPort),
            parseResult.GetValue(image))));
        return command;
    }

    private sealed record InitRequest(DirectoryInfo Output, string? Domain, bool Local, string? PublicUrl, string? HttpPort, string? Image);

    /// <summary>What <c>.env</c> holds besides the passwords, worked out from the flags before anything is written.</summary>
    private sealed record InstallSettings(string Profile, string Domain, string PublicUrl, int? HttpPort, string? Image, string Version);

    private static int Run(InitRequest request)
    {
        if (Settings(request) is not { } settings)
        {
            return 1;
        }

        string bundle = Path.Combine(AppContext.BaseDirectory, "install");
        if (BundleFiles.FirstOrDefault(file => !File.Exists(Path.Combine(bundle, file))) is { } missing)
        {
            Console.Error.WriteLine($"The install bundle is incomplete: {Path.Combine(bundle, missing)} is missing.");
            Console.Error.WriteLine("Run init from the Orvano image, which carries the bundle in /app/install/.");
            return 1;
        }

        DirectoryInfo output = request.Output;
        string keyPath = Path.Combine(output.FullName, "root.key");
        string envPath = Path.Combine(output.FullName, ".env");

        // Spec 0007 INS-5, step 1, widened by spec 0016 BND-3: refuse before changing anything, not even the folder's
        // mode, and name every path that is in the way.
        string[] existing = [.. new[] { "compose.yaml", "Caddyfile", PostgresInit, ".env", "root.key" }
            .Select(name => Path.Combine(output.FullName, name))
            .Where(path => File.Exists(path) || Directory.Exists(path))];
        if (existing.Length > 0)
        {
            foreach (string path in existing)
            {
                Console.Error.WriteLine($"{path} already exists.");
            }

            Console.Error.WriteLine("init never overwrites an install; use an empty folder, or move these away first.");
            return 1;
        }

        try
        {
            output.Create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot write to {output.FullName}: {ex.Message}");
            Console.Error.WriteLine(UserHint);
            return 1;
        }

        if (!OperatingSystem.IsWindows() && !RestrictFolder(output.FullName))
        {
            return 1;
        }

        // Step 4: the bundle, .env, then root.key; anything this run created goes again on failure (BND-2), so a retry
        // is not refused by BND-3.
        var created = new List<string>();
        try
        {
            string folder = Path.Combine(output.FullName, PostgresInit);
            Directory.CreateDirectory(folder);
            created.Add(folder);
            SetMode(folder, Executable);

            foreach (string file in BundleFiles)
            {
                string path = Path.Combine(output.FullName, file);
                Write(path, File.ReadAllBytes(Path.Combine(bundle, file)), created);
                SetMode(path, file.EndsWith(".sh", StringComparison.Ordinal) ? Executable : Readable);
            }

            Write(envPath, Encoding.UTF8.GetBytes(EnvironmentFile(settings)), created);
            Write(keyPath, Encoding.UTF8.GetBytes(RootKey.Generate() + "\n"), created);
            SetMode(keyPath, Readable); // explicit, so a strict umask cannot leave it at 0600
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Newest first, so the folder goes after the file inside it.
            foreach (string path in Enumerable.Reverse(created))
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }

            Console.Error.WriteLine($"Cannot write to {output.FullName}: {ex.Message}");
            Console.Error.WriteLine(UserHint);
            return 1;
        }

        Console.WriteLine($"Wrote compose.yaml, Caddyfile, postgres-init/, .env, and root.key to {output.FullName}.");
        Console.WriteLine($"This install runs {settings.Image ?? DefaultImage}:{settings.Version} in {settings.Profile} mode at {settings.PublicUrl}.");
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("File modes are skipped on Windows; keep this folder private to your account.");
        }

        Console.WriteLine();
        Console.WriteLine("BACK UP root.key NOW, and keep it apart from your database backups.");
        Console.WriteLine("It encrypts every stored secret. If you lose it, those secrets cannot be recovered.");
        Console.WriteLine("root.key is safe only inside this folder. Make any copy owner only (chmod 600) or encrypt it.");
        Console.WriteLine("Run docker compose as the same user that ran init.");
        return 0;
    }

    /// <summary>
    /// Spec 0016 BND-4, LCL-3, PIN-2: checks the flags and works out the mode's values. Writes the reason and returns
    /// null when they do not fit together.
    /// </summary>
    private static InstallSettings? Settings(InitRequest request)
    {
        if (request.Local == request.Domain is not null)
        {
            Console.Error.WriteLine("Pass exactly one of --domain <domain> (a server with HTTPS) or --local (this machine only).");
            return null;
        }

        if (request.Image is { } image && !ImageRepository().IsMatch(image))
        {
            Console.Error.WriteLine($"--image must be an image repository without a tag, for example {DefaultImage}.");
            return null;
        }

        string? customImage = request.Image is null or DefaultImage ? null : request.Image;

        string version = Environment.GetEnvironmentVariable(ImageVersionVariable) is { Length: > 0 } fromImage ? fromImage : "dev";
        if (!ImageTag().IsMatch(version))
        {
            Console.Error.WriteLine($"{ImageVersionVariable} '{version}' is not a valid image tag.");
            return null;
        }

        if (request.Local)
        {
            if (request.PublicUrl is not null)
            {
                Console.Error.WriteLine("--public-url works with --domain only; local mode always uses http://localhost.");
                return null;
            }

            int port = 80;
            if (request.HttpPort is { } portText
                && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            {
                Console.Error.WriteLine("--http-port must be a port number from 1 to 65535.");
                return null;
            }

            string publicUrl = port == 80 ? "http://localhost" : $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}";
            return new InstallSettings("local", "localhost", publicUrl, port == 80 ? null : port, customImage, version);
        }

        string domain = request.Domain!;
        if (request.HttpPort is not null)
        {
            // PRT-1: Caddy's automatic certificates need the challenge on the standard ports.
            Console.Error.WriteLine("--http-port works with --local only; server mode always uses ports 80 and 443.");
            return null;
        }

        if (Uri.CheckHostName(domain) is UriHostNameType.Unknown)
        {
            Console.Error.WriteLine($"'{domain}' is not a valid domain.");
            return null;
        }

        if (IPAddress.TryParse(domain.Trim('[', ']'), out _)
            || domain.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || !domain.Contains('.', StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"'{domain}' cannot get a public HTTPS certificate: use a full domain name such as orvano.example.com.");
            Console.Error.WriteLine(LocalHint);
            return null;
        }

        if (!Uri.TryCreate(request.PublicUrl ?? $"https://{domain}", UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps)
        {
            Console.Error.WriteLine("--public-url must be an absolute https URL.");
            return null;
        }

        return new InstallSettings("server", domain, url.GetLeftPart(UriPartial.Authority), null, customImage, version);
    }

    /// <summary>Spec 0007 INS-5, steps 2 and 3: the folder becomes 0700, and the mode must read back.</summary>
    [UnsupportedOSPlatform("windows")]
    private static bool RestrictFolder(string folder)
    {
        try
        {
            File.SetUnixFileMode(folder, FolderMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Spec 0007 INS-3: the usual cause on Linux is init without --user, so this message names that form too.
            Console.Error.WriteLine($"Cannot set {folder} to mode 0700: {ex.Message}");
            Console.Error.WriteLine(UserHint);
            Console.Error.WriteLine(FolderHint);
            return false;
        }

        UnixFileMode mode = File.GetUnixFileMode(folder) & AllPermissions;
        if (mode != FolderMode)
        {
            Console.Error.WriteLine($"{folder} stays at mode {Octal(mode)} instead of 0700, so root.key could not be protected.");
            Console.Error.WriteLine(FolderHint);
            return false;
        }

        Console.WriteLine($"Set {folder} to mode 0700 (only you can open it).");
        return true;
    }

    private static string EnvironmentFile(InstallSettings settings)
    {
        string created = TimeProvider.System.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        StringBuilder text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# Written by orvano init on {created}. Keep this file private; it holds passwords.")
            .AppendLine("# .env.example in the Orvano repository's deploy/ folder explains every variable.")
            .AppendLine(CultureInfo.InvariantCulture, $"COMPOSE_PROFILES={settings.Profile}")
            .AppendLine(CultureInfo.InvariantCulture, $"ORVANO_VERSION={settings.Version}");
        if (settings.Image is { } image)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"ORVANO_IMAGE={image}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"ORVANO_DOMAIN={settings.Domain}")
            .AppendLine(CultureInfo.InvariantCulture, $"ORVANO_PUBLIC_URL={settings.PublicUrl}");
        if (settings.HttpPort is { } port)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"ORVANO_HTTP_PORT={port}");
        }

        if (settings.Profile == "local")
        {
            text.AppendLine("ORVANO_INSECURE_COOKIES=true");
        }

        return text
            .AppendLine(CultureInfo.InvariantCulture, $"POSTGRES_PASSWORD={NewPassword()}")
            .AppendLine(CultureInfo.InvariantCulture, $"ORVANO_DB_PASSWORD={NewPassword()}")
            .AppendLine(CultureInfo.InvariantCulture, $"ORVANO_PROVISIONER_PASSWORD={NewPassword()}")
            .ToString();
    }

    private static string NewPassword() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static void Write(string path, byte[] contents, List<string> created)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        using var stream = new FileStream(path, options);
        created.Add(path);
        stream.Write(contents);
    }

    private static void SetMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    private static string Octal(UnixFileMode mode) => "0" + Convert.ToString((int)mode, 8);

    // A registry host (with an optional port) and path, lowercase, no tag or digest: what compose puts before ':<version>'.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9._-]*[a-z0-9])?(:[0-9]{1,5})?(/[a-z0-9]([a-z0-9._-]*[a-z0-9])?)*$")]
    private static partial Regex ImageRepository();

    // The OCI tag grammar.
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex ImageTag();
}
