using System.Text;
using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>
/// Runner operations: calls the scenarios make that are not contract operations. <c>signIn</c> is a plain sign in
/// call, so this runner (the .NET SDK has no client operations) gets a token too; <c>verifyAccessToken</c> is the
/// SDK's own check; <c>now</c> is the runner's clock, saved before a send and passed to <c>test.getLatestEmail</c>
/// as <c>after</c>. Their names have no dot, so they never collide with an operationId.
/// </summary>
internal static class RunnerDispatch
{
    public static readonly IReadOnlyDictionary<string, DispatchEntry> Operations = new Dictionary<string, DispatchEntry>(StringComparer.Ordinal)
    {
        ["now"] = new(200, (_, _, _) =>
            Task.FromResult<JsonNode?>(new JsonObject { ["now"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }), null),
        // The link helper is a client SDK feature; the .NET SDK has no client operations, so the scenario skips.
        ["redeemLink"] = new(200, null, null),
        ["signIn"] = new(201, async (client, input, ct) =>
        {
            var body = Encoding.UTF8.GetBytes(input["body"]?.ToJsonString() ?? "{}");
            var text = await client.SendForTextAsync(new OrvanoRequest("POST", "/v1/account/sessions/password", null, body, false), ct);
            return JsonNode.Parse(text);
        }, null),
        ["verifyAccessToken"] = new(200, async (client, input, ct) =>
        {
            var verified = await client.VerifyAccessTokenAsync(
                input["token"]?.GetValue<string>() ?? "", input["online"]?.GetValue<bool>() == true, ct);
            return new JsonObject
            {
                ["userId"] = verified.UserId,
                ["sessionId"] = verified.SessionId,
                ["emailVerified"] = verified.EmailVerified,
                ["expiresAt"] = verified.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            };
        }, null),
    };
}
