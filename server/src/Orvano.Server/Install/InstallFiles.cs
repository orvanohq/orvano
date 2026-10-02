using System.Text;

namespace Orvano.Server.Install;

/// <summary>
/// The files in the install directory (spec 0006, Install layout). Every file is written to a
/// temporary name and renamed over the old one, so a crash never leaves half a file.
/// <c>docker-compose.override.yml</c> is never read, written, or deleted.
/// </summary>
internal sealed class InstallFiles(string directory)
{
    public const string ComposeFile = "docker-compose.yml";
    public const string LocalComposeFile = "docker-compose.local.yml";
    public const string InitdbScript = "initdb/10-orvano-roles.sh";
    public const string Env = ".env";
    public const string PreviousEnv = ".env.previous";
    public const string Log = "install.log";
    public const string Result = ".install-result";

    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode Public = Private | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode Executable = Public | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public string PathOf(string file) => Path.Combine(directory, file);

    public string? ReadEnv() => File.Exists(PathOf(Env)) ? File.ReadAllText(PathOf(Env)) : null;

    /// <summary>
    /// Rewrites the compose file and the init script from the copies embedded in this image (AC-10), plus the local
    /// overlay for a local install (spec 0011).
    /// </summary>
    public async Task WriteManagedFilesAsync(bool local = false)
    {
        var initdb = Path.GetDirectoryName(PathOf(InitdbScript))!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(initdb);
        else Directory.CreateDirectory(initdb, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        await WriteAsync(ComposeFile, await ReadResourceAsync("install/docker-compose.yml"), Public);
        await WriteAsync(InitdbScript, await ReadResourceAsync("install/initdb/10-orvano-roles.sh"), Executable);
        if (local) await WriteAsync(LocalComposeFile, await ReadResourceAsync("install/docker-compose.local.yml"), Public);
    }

    /// <summary>Writes <c>.env</c> (0600), keeping the file it replaces as <c>.env.previous</c>.</summary>
    public async Task WriteEnvAsync(string content, string? previous)
    {
        if (previous is not null) await WriteAsync(PreviousEnv, previous, Private);
        await WriteAsync(Env, content, Private);
    }

    /// <summary>
    /// Writes <c>.install-result</c> (0600), which <c>install.sh</c> reads and deletes to learn what
    /// this run did. A file, not a printed line, so you never see it and prompts reach you unfiltered.
    /// </summary>
    public Task WriteResultAsync(bool generatedMasterKey) =>
        WriteAsync(Result, $"generated_master_key={(generatedMasterKey ? 1 : 0)}\n", Private);

    /// <summary>Appends one timestamped line to <c>install.log</c> (0600). Never pass a secret.</summary>
    public async Task LogAsync(DateTimeOffset now, string message)
    {
        var path = PathOf(Log);
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = Private;
        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"{now.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} install: {message}\n"));
    }

    private async Task WriteAsync(string file, string content, UnixFileMode mode)
    {
        var path = PathOf(file);
        var temp = path + ".tmp";
        File.Delete(temp);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = mode;
        await using (var stream = new FileStream(temp, options))
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
            await stream.FlushAsync();
            stream.Flush(flushToDisk: true);
        }

        // The create mode is masked by the umask; set it exactly.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, mode);
        File.Move(temp, path, overwrite: true);
    }

    private static async Task<string> ReadResourceAsync(string name)
    {
        await using var stream = typeof(InstallFiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded file {name} is missing from this image.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
