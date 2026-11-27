// The orvano executor. Its health check arrives with feature 4, the sandbox with feature 54.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.Run();
