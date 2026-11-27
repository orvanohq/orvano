// The orvano host. It wires every module into one process.
// Health endpoints, logging setup and the first routes arrive with feature 4.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.Run();
