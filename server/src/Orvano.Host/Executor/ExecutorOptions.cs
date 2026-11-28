namespace Orvano.Host.Executor;

/// <summary>
/// Where <c>orvano</c> reaches <c>orvano-executor</c>. Bound from the <c>Executor</c> section and validated at startup.
/// Nothing calls the executor yet; feature 54 moves this to wherever the executor client lives.
/// </summary>
public sealed class ExecutorOptions
{
    public const string Section = "Executor";

    /// <summary>An absolute <c>http</c> address, such as <c>http://orvano-executor:8080</c>.</summary>
    public string? BaseUrl { get; set; }

    internal const string InvalidBaseUrlMessage =
        "The setting Executor:BaseUrl must be an absolute http address, such as http://orvano-executor:8080. Set Executor__BaseUrl.";

    internal static bool HasValidBaseUrl(ExecutorOptions options) =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp;
}

internal static class ExecutorOptionsRegistration
{
    public static IServiceCollection AddExecutorOptions(this IServiceCollection services)
    {
        services.AddOptions<ExecutorOptions>()
            .BindConfiguration(ExecutorOptions.Section)
            .Validate(ExecutorOptions.HasValidBaseUrl, ExecutorOptions.InvalidBaseUrlMessage)
            .ValidateOnStart();

        return services;
    }
}
