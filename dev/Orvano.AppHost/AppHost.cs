// Local dev: one command starts Postgres 18, runs migrate to completion, then starts the api,
// worker, and realtime roles and the console dev server (spec 0002). Production uses
// deploy/compose instead; there is no Caddy here, the Vite dev server proxies /v1.

var builder = DistributedApplication.CreateBuilder(args);

// Optional: pass `-- --OrvanoDev:Fixtures=true` to run api and worker in Test with the shared
// scenario fixtures, and to give the console dev server the fixture console session, so the dev
// console opens straight into the Fixtures org (spec 0005). Row 8 removes this switch.
var useFixtures = bool.TryParse(builder.Configuration["OrvanoDev:Fixtures"], out var fixtures) && fixtures;
var fixturesPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "scenarios", "fixtures.yaml"));

// The first `consoleSessions` entry in tests/scenarios/fixtures.yaml. The duplication is accepted:
// a mismatch fails loudly (the dev console lands on /sign-in), and row 8 deletes both.
const string ConsoleDevSession = "test-console-session";

var noSymbols = new GenerateParameterDefault { MinLength = 32, Special = false };
var adminPassword = builder.AddParameter("orvano-admin-password", noSymbols, secret: true, persist: true);
var appPassword = builder.AddParameter("orvano-app-password", noSymbols, secret: true, persist: true);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18.6")
    .WithEnvironment("ORVANO_ADMIN_PASSWORD", adminPassword)
    .WithEnvironment("ORVANO_APP_PASSWORD", appPassword)
    .WithInitFiles("../../deploy/compose/initdb");

var endpoint = postgres.Resource.PrimaryEndpoint;
var adminDb = ReferenceExpression.Create(
    $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Username=orvano_admin;Password={adminPassword};Database=orvano");
var appDb = ReferenceExpression.Create(
    $"Host={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};Username=orvano_app;Password={appPassword};Database=orvano");

// One project, started once per role. No launch profile, so each role gets its own port.
IResourceBuilder<ProjectResource> Role(string role)
{
    var fixtureRole = useFixtures && role is "api" or "worker";
    var project = builder.AddProject<Projects.Orvano_Server>(role, launchProfileName: null)
        .WithArgs(role)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", fixtureRole ? "Test" : "Development")
        .WithEnvironment("OTEL_SERVICE_NAME", $"orvano-{role}")
        .WaitFor(postgres);
    return fixtureRole
        ? project.WithEnvironment("ORVANO_TEST_FIXTURES", fixturesPath)
        : project;
}

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

var console = builder.AddViteApp("console", "../../console")
    .WithPnpm()
    .WithReference(api)
    .WithReference(realtime)
    .WaitFor(api);

if (useFixtures)
{
    console.WithEnvironment("CONSOLE_DEV_SESSION", ConsoleDevSession);
}

builder.Build().Run();
