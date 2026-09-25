using System.Diagnostics;

namespace Orvano.SdkGen.Rendering;

/// <summary>Writes generated files, removes stale ones, then runs each language's pinned formatter.</summary>
internal static class OutputWriter
{
    public static async Task WriteAsync(string root, IReadOnlyList<GeneratedOutput> outputs)
    {
        foreach (var output in outputs)
        {
            var directory = Path.Combine(root, output.Directory);
            Directory.CreateDirectory(directory);
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in output.Files)
            {
                var path = Path.GetFullPath(Path.Combine(root, file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content.ReplaceLineEndings("\n"));
                written.Add(path);
            }

            foreach (var stale in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath).Where(p => !written.Contains(p)))
            {
                File.Delete(stale);
                Console.WriteLine($"  removed {Path.GetRelativePath(root, stale)}");
            }

            Console.WriteLine($"  {output.Name}: {output.Files.Count} files in {output.Directory}");
        }

        foreach (var group in outputs.GroupBy(o => o.Formatter))
        {
            var directories = group.Select(o => o.Directory).ToList();
            switch (group.Key)
            {
                case Formatter.Prettier:
                    await RunAsync(root, "pnpm", ["exec", "prettier", "--write", "--log-level", "warn", .. directories]);
                    break;
                case Formatter.Dart:
                    await RunAsync(root, "dart", ["format", "--output", "write", "--show", "none", .. directories]);
                    break;
                case Formatter.CSharp:
                    foreach (var d in directories)
                        await RunAsync(root, "dotnet", ["format", "whitespace", d, "--folder"]);
                    break;
                default:
                    throw new InvalidOperationException($"unknown formatter {group.Key}");
            }
        }
    }

    private static async Task RunAsync(string root, string tool, IReadOnlyList<string> args)
    {
        // On Windows, pnpm and dart are .cmd/.bat shims that need the shell.
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", tool, .. args])
            : new ProcessStartInfo(tool, args);
        info.WorkingDirectory = root;
        info.RedirectStandardError = true;

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {tool}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"'{tool}' was not found on PATH; SdkGen needs it to format generated code");
        }

        using (process)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{tool} {string.Join(' ', args)} failed ({process.ExitCode}): {stderr}");
        }
    }
}
