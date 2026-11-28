// The orvano executor. The sandbox arrives with feature 54.

// The container health probe runs before any host is built (spec 0002, "The healthcheck argument").
if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapLiveness();

await app.RunAsync();
return 0;
