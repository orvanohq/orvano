using System.Reflection;

namespace Orvano.Server.Hosting;

internal static class OrvanoVersion
{
    /// <summary>From the repo's VERSION file, stamped at build time.</summary>
    public static string Current { get; } =
        typeof(OrvanoVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}
