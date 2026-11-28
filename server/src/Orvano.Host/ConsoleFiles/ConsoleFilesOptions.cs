namespace Orvano.Host.ConsoleFiles;

/// <summary>
/// Where <c>orvano</c> finds the exported console it serves at <c>/console</c> (spec 0003).
/// Bound from the <c>Console</c> section and validated at startup.
/// </summary>
public sealed class ConsoleFilesOptions
{
    public const string Section = "Console";

    /// <summary>
    /// The folder that holds the console's static export. Relative paths resolve against the content root,
    /// so the default <c>console</c> is <c>/app/console</c> in the image. Point it at <c>console/out</c> to serve a local build.
    /// </summary>
    public string RootPath { get; set; } = "console";

    internal const string InvalidRootPathMessage =
        "The setting Console:RootPath must be a folder path, such as console or /app/console. Set Console__RootPath.";

    internal static bool HasValidRootPath(ConsoleFilesOptions options) => !string.IsNullOrWhiteSpace(options.RootPath);
}

internal static class ConsoleFilesOptionsRegistration
{
    public static IServiceCollection AddConsoleFilesOptions(this IServiceCollection services)
    {
        services.AddOptions<ConsoleFilesOptions>()
            .BindConfiguration(ConsoleFilesOptions.Section)
            .Validate(ConsoleFilesOptions.HasValidRootPath, ConsoleFilesOptions.InvalidRootPathMessage)
            .ValidateOnStart();

        return services;
    }
}
