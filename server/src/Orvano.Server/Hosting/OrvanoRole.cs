namespace Orvano.Server.Hosting;

internal enum OrvanoRole { Api, Worker, Realtime, Migrate, Executor }

internal sealed record RoleSelection(OrvanoRole Role, string[] RemainingArgs);

internal static class RoleSelector
{
    private static readonly Dictionary<string, OrvanoRole> Names = new(StringComparer.Ordinal)
    {
        ["api"] = OrvanoRole.Api,
        ["worker"] = OrvanoRole.Worker,
        ["realtime"] = OrvanoRole.Realtime,
        ["migrate"] = OrvanoRole.Migrate,
        ["executor"] = OrvanoRole.Executor,
    };

    public static string Name(this OrvanoRole role) => role.ToString().ToLowerInvariant();

    /// <summary>
    /// The role comes from <c>ORVANO_ROLE</c> or the first argument. There is no default: unset,
    /// unknown, or the two disagreeing is an error (spec 0002, roles).
    /// </summary>
    public static RoleSelection Resolve(string[] args, string? envRole, out string? error)
    {
        var argRole = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null;
        var rest = argRole is null ? args : args[1..];
        var chosen = argRole ?? (string.IsNullOrWhiteSpace(envRole) ? null : envRole);
        error = null;

        if (chosen is null)
            error = $"No role given. Set ORVANO_ROLE or pass one of: {string.Join(", ", Names.Keys)}.";
        else if (argRole is not null && !string.IsNullOrWhiteSpace(envRole) && envRole != argRole)
            error = $"ORVANO_ROLE is '{envRole}' but the argument says '{argRole}'. Use one, or make them match.";
        else if (!Names.ContainsKey(chosen))
            error = $"Unknown role '{chosen}'. Expected one of: {string.Join(", ", Names.Keys)}.";

        return new RoleSelection(error is null ? Names[chosen!] : default, rest);
    }
}
