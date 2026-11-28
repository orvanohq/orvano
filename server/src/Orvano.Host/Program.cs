// The orvano host. It wires every module into one process.
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Orvano.Host.ConsoleFiles;
using Orvano.Host.Executor;
using Orvano.Infrastructure.Database;

// The container health probe runs before any host is built (spec 0002, "The healthcheck argument").
if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOrvanoDatabase(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<PostgresReadinessCheck>(PostgresReadinessCheck.Name, tags: [ReadinessTag.Ready]);

builder.Services.AddExecutorOptions();
builder.Services.AddConsoleFilesOptions();

var app = builder.Build();

app.MapLiveness();
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(ReadinessTag.Ready),
    ResponseWriter = HealthEndpoints.WriteResponseAsync,
});
app.MapConsoleFiles();

await app.RunAsync();
return 0;

/// <summary>The tag that puts a health check into <c>/readyz</c>.</summary>
internal static class ReadinessTag
{
    public const string Ready = "ready";
}
