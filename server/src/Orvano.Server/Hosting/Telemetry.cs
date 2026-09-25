using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orvano.Core.Events;

namespace Orvano.Server.Hosting;

internal static class Telemetry
{
    /// <summary>
    /// OpenTelemetry traces, metrics, and logs for every role. JSON logs go to stdout; OTLP export is
    /// on only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.
    /// </summary>
    public static void AddOrvanoTelemetry(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Logging.ClearProviders();
        if (builder.Environment.IsDevelopment())
            builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        else
            builder.Logging.AddJsonConsole(o => o.UseUtcTimestamp = true);

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: OrvanoVersion.Current))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/internal"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql", EventTelemetry.MeterName, RealtimeFanout.MeterName));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();
    }
}
