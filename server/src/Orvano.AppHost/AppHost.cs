// Contributor orchestration: Postgres 17, the orvano host and the executor (spec 0002, "Contributor AppHost").
// No Caddy here: the Compose smoke test exercises the Caddyfile.
var builder = DistributedApplication.CreateBuilder(args);

// Not named "orvano": Aspire resource names are unique and the host already uses it.
var database = builder.AddPostgres("postgres")
    .WithImageTag("17")
    .AddDatabase("orvano-db", databaseName: "orvano");

var executor = builder.AddProject<Projects.Orvano_Executor>("orvano-executor")
    .WithHttpHealthCheck("/healthz");

builder.AddProject<Projects.Orvano_Host>("orvano")
    .WithReference(database, connectionName: "orvano")
    .WaitFor(database)
    .WithHttpHealthCheck("/readyz")
    .WithEnvironment("Executor__BaseUrl", executor.GetEndpoint("http"));

builder.Build().Run();
