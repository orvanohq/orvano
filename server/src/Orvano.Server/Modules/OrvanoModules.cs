using Orvano.Core.Modules;

namespace Orvano.Server.Modules;

/// <summary>Every module the server runs, listed explicitly (no assembly scanning). Each scope row adds its own.</summary>
internal static class OrvanoModules
{
    public static IReadOnlyList<IOrvanoModule> All { get; } =
    [
        new SystemModule(),
    ];
}
