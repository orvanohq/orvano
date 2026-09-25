// Local dev: one command starts Postgres 18, runs migrate to completion, then starts the api,
// worker, and realtime roles and the console dev server (spec 0002). Production uses
// deploy/compose instead; there is no Caddy here, the Vite dev server proxies /v1.

var builder = DistributedApplication.CreateBuilder(args);

var noSymbols = new GenerateParameterDefault { MinLength = 32, Special = false };
var adminPassword = builder.AddParameter("orvano-admin-password", noSymbols, secret: true, persist: true);
var appPassword = builder.AddParameter("orvano-app-password", noSymbols, secret: true, persist: true);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithEnvironment("ORVANO_ADMIN_PASSWORD", adminPassword)
    .WithEnvironment("ORVANO_APP_PASSWORD", appPassword)
    .WithInitFiles("../../deploy/postgres/initdb");

var endpoint = postgres.Resource.PrimaryEndpoint;
var adminDb = ReferenceExpression.Create(
    $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Username=orvano_admin;Password={adminPassword};Database=orvano");
var appDb = ReferenceExpression.Create(
    $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Username=orvano_app;Password={appPassword};Database=orvano");

// One project, started once per role. No launch profile, so each role gets its own port.
IResourceBuilder<ProjectResource> Role(string role) =>
    builder.AddProject<Projects.Orvano_Server>(role, launchProfileName: null)
        .WithArgs(role)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("OTEL_SERVICE_NAME", $"orvano-{role}")
        .WaitFor(postgres);

var migrate = Role("migrate")
    .WithEnvironment("ORVANO_DB_ADMIN_URL", adminDb);

IResourceBuilder<ProjectResource> LongRunning(string role) =>
    Role(role)
        .WithHttpEndpoint(name: "http")
        .WithEnvironment("ORVANO_DB_URL", appDb)
        .WithHttpHealthCheck("/internal/readyz")
        .WaitForCompletion(migrate);

var api = LongRunning("api");
var realtime = LongRunning("realtime");
LongRunning("worker").WithEnvironment("ORVANO_DB_ADMIN_URL", adminDb);

builder.AddViteApp("console", "../../console")
    .WithPnpm()
    .WithReference(api)
    .WithReference(realtime)
    .WaitFor(api);

builder.Build().Run();
