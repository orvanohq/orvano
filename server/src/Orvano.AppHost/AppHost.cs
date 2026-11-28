// Contributor orchestration: Postgres 17, the orvano host, the executor (spec 0002, "Contributor AppHost")
// and the console dev server (spec 0003, "Contributor dev server").
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

// The console's Next.js dev server, with hot reload. It references nothing: the console makes no API calls yet.
// next dev reads PORT; the dashboard link opens /console/, the base path.
builder.AddJavaScriptApp("console", "../../../console", "dev")
    .WithPnpm()
    .WithHttpEndpoint(env: "PORT")
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.Url = new UriBuilder(url.Url) { Path = "/console/" }.ToString();
        }
    });

builder.Build().Run();
