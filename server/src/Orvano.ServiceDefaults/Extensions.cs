using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Defaults shared by <c>orvano</c> and <c>orvano-executor</c>: OpenTelemetry, JSON console logs
/// outside Development, and the health check service that <c>/healthz</c> and <c>/readyz</c> need.
/// Export is off unless <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (the AppHost sets it for contributors).
/// </summary>
public static class Extensions
{
    private static readonly string[] HealthPaths = ["/healthz", "/readyz"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureJsonConsoleLogging();
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>
    /// One JSON object per stdout line, with <c>Timestamp</c> (UTC, ISO 8601), <c>LogLevel</c>, <c>Category</c>
    /// and <c>Message</c>. Development keeps the readable console.
    /// </summary>
    public static TBuilder ConfigureJsonConsoleLogging<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Logging.AddJsonConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.IncludeScopes = true;
            // The formatter writes no Timestamp field at all without a format.
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        // Keep health probes out of traces.
                        options.Filter = context =>
                            !HealthPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
                    .AddHttpClientInstrumentation();
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
