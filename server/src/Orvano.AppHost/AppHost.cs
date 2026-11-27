// Contributor orchestration: Postgres 17, the orvano host and the executor.
// Health checks, readiness and the Compose file arrive with feature 4.
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPostgres("postgres")
    .WithImageTag("17");

builder.AddProject<Projects.Orvano_Host>("orvano");

builder.AddProject<Projects.Orvano_Executor>("orvano-executor");

builder.Build().Run();
