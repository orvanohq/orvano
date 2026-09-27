namespace Orvano.Server.Install;

/// <summary>
/// The flags of <c>orvano install</c> (spec 0006, Command line). <c>install.sh</c> passes every flag
/// through untouched, so the flags only the script uses (<c>--dir</c>, <c>--no-pull</c>,
/// <c>--timeout</c>) are accepted and ignored here. <c>--existing-data</c> comes from the script alone.
/// </summary>
internal sealed record InstallOptions(
    string? Domain,
    string? Email,
    string? Version,
    bool Yes,
    bool NoIpLookup,
    bool? ExistingData)
{
    private static readonly HashSet<string> ValueFlags = ["--domain", "--email", "--version", "--dir", "--timeout", "--existing-data"];
    private static readonly HashSet<string> Switches = ["--yes", "--no-ip-lookup", "--no-pull"];

    /// <summary>Parses <c>--flag value</c> and <c>--flag=value</c>; returns null and an error for anything else.</summary>
    public static InstallOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var equals = arg.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? arg[..equals] : arg;

            if (Switches.Contains(arg))
            {
                switches.Add(arg);
            }
            else if (ValueFlags.Contains(name))
            {
                string? value = equals > 0 ? arg[(equals + 1)..] : i + 1 < args.Count ? args[++i] : null;
                if (value is null)
                {
                    error = $"{name} needs a value.";
                    return null;
                }

                values[name] = value;
            }
            else
            {
                error = $"Unknown flag '{arg}'. Run install.sh --help to see every flag.";
                return null;
            }
        }

        bool? existingData = null;
        if (values.TryGetValue("--existing-data", out var existing))
        {
            existingData = existing switch
            {
                "yes" => true,
                "no" => false,
                _ => null,
            };
            if (existingData is null)
            {
                error = "--existing-data must be yes or no.";
                return null;
            }
        }

        error = null;
        return new InstallOptions(
            values.GetValueOrDefault("--domain"),
            values.GetValueOrDefault("--email"),
            values.GetValueOrDefault("--version"),
            switches.Contains("--yes"),
            switches.Contains("--no-ip-lookup"),
            existingData);
    }
}
