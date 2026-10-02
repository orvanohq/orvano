using Orvano;

// #region client
// The server client. It sends your API key with every call and checks your users' access tokens against the
// project's public keys. Settings come from the environment, so the key never sits in code.
var endpoint = new Uri(Setting("ORVANO_ENDPOINT", "http://localhost:7700"));
using var orvano = new OrvanoClient(new OrvanoClientOptions(endpoint)
{
    Project = Setting("ORVANO_PROJECT"),
    ApiKey = Setting("ORVANO_API_KEY"),
});
// #endregion client

// #region create-user
// `dotnet run -- create-user <email> <password> [name]` creates a user with the API key, then exits.
if (args is ["create-user", var email, var password, ..])
{
    try
    {
        var created = await orvano.Users.CreateAsync(
            new CreateUserRequest(email, password, Name: args.Length > 3 ? args[3] : null));
        Console.WriteLine($"Created user {created.Id} ({created.Email}).");
        return 0;
    }
    catch (OrvanoException error)
    {
        Console.Error.WriteLine($"Orvano refused: {error.Message} ({error.Code})");
        return 1;
    }
}
// #endregion create-user

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:3002");
builder.Services.AddSingleton(orvano);
var app = builder.Build();

// #region me
// GET /me checks the access token in `Authorization: Bearer <token>`, then answers with the user it belongs to.
// A missing or bad token gets 401.
app.MapGet("/me", async (HttpContext context, OrvanoClient orvano, CancellationToken cancellationToken) =>
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Unauthorized(context);

    try
    {
        // Checks the signature, the project, and the expiry, without a call to Orvano once the keys are cached.
        var token = await orvano.VerifyAccessTokenAsync(
            header["Bearer ".Length..], cancellationToken: cancellationToken);
        var user = await orvano.Users.GetAsync(token.UserId, cancellationToken);
        return Results.Ok(new { user.Id, user.Email, user.Name });
    }
    catch (OrvanoException error) when (error.Status == StatusCodes.Status401Unauthorized)
    {
        // invalid_token or token_expired; anything else is a real failure.
        return Unauthorized(context);
    }
});

static IResult Unauthorized(HttpContext context)
{
    context.Response.Headers.WWWAuthenticate = "Bearer";
    return Results.Json(
        new { error = "Send a valid access token." }, statusCode: StatusCodes.Status401Unauthorized);
}
// #endregion me

app.Run();
return 0;

static string Setting(string name, string? fallback = null)
{
    var value = Environment.GetEnvironmentVariable(name) ?? fallback;
    return string.IsNullOrEmpty(value)
        ? throw new InvalidOperationException($"Set the {name} environment variable.")
        : value;
}
