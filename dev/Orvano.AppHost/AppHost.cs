// Local dev: one command starts Postgres 18, runs migrate to completion, then starts the api,
// worker, and realtime roles and the console dev server (spec 0002). Production uses
// deploy/compose instead; there is no Caddy here, the Vite dev server proxies /v1.

var builder = DistributedApplication.CreateBuilder(args);

// The console starts with no account: open /setup on the console dev server to create the first
// admin (no setup token is set here, so the first sign up needs none), then sign in as usual.

var noSymbols = new GenerateParameterDefault { MinLength = 32, Special = false };
var adminPassword = builder.AddParameter("orvano-admin-password", noSymbols, secret: true, persist: true);
var appPassword = builder.AddParameter("orvano-app-password", noSymbols, secret: true, persist: true);

// The dev install's master key (spec 0002): 42 random letters and digits plus `A`, read as base64url
// of 32 bytes (the last character's 2 unused bits must be zero), kept across runs so the local
// database's encrypted secrets stay readable.
var masterKey = builder.AddParameter("orvano-master-key",
    new GenerateParameterDefault { MinLength = 42, Special = false }, secret: true, persist: true);
var masterKeys = ReferenceExpression.Create($"kdev:{masterKey}A");

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

// Mailpit catches every email the dev install sends (spec 0009): point a project's SMTP settings at
// host `localhost`, the port of its `smtp` endpoint, security None, and read the mail on its `http`
// endpoint. It is on a private address, so the roles that send are told to allow those.
builder.AddContainer("mailpit", "axllent/mailpit", "v1.31.3")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithHttpEndpoint(targetPort: 8025, name: "http");

var api = LongRunning("api")
    .WithEnvironment("ORVANO_MASTER_KEYS", masterKeys)
    .WithEnvironment("ORVANO_SMTP_ALLOW_PRIVATE_HOSTS", "true");
var realtime = LongRunning("realtime");
LongRunning("worker")
    .WithEnvironment("ORVANO_DB_ADMIN_URL", adminDb)
    .WithEnvironment("ORVANO_MASTER_KEYS", masterKeys)
    .WithEnvironment("ORVANO_SMTP_ALLOW_PRIVATE_HOSTS", "true");

var console = builder.AddViteApp("console", "../../console")
    .WithPnpm()
    .WithReference(api)
    .WithReference(realtime)
    .WaitFor(api);

// The console dev server is the install's public address here: it proxies /v1 to the api, so the
// console stays same origin and access tokens name it as their issuer.
api.WithEnvironment("ORVANO_PUBLIC_URL", console.GetEndpoint("http"));

builder.Build().Run();
