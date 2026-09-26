using Orvano.Core.Modules;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>Every module the server runs, listed explicitly (no assembly scanning). Each scope row adds its own.</summary>
internal static class OrvanoModules
{
    /// <summary>The product modules, in every environment.</summary>
    public static IReadOnlyList<IOrvanoModule> Product { get; } =
    [
        new SystemModule(),
    ];

    /// <summary>
    /// The modules for this environment: the product modules, plus the test only operations in the
    /// <c>Test</c> environment (spec 0001, AC-18). Anywhere else their routes do not exist.
    /// </summary>
    public static IReadOnlyList<IOrvanoModule> For(IHostEnvironment environment) =>
        environment.IsEnvironment(OrvanoEnvironments.Test) ? [.. Product, new TestingModule()] : Product;
}
