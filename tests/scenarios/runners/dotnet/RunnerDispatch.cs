using System.Text;
using System.Text.Json.Nodes;

namespace Orvano.Scenarios;

/// <summary>
/// Runner operations: calls the scenarios make that are not contract operations. <c>signIn</c> is a plain sign in
/// call, so this runner (the .NET SDK has no client operations) gets a token too; <c>verifyAccessToken</c> is the
/// SDK's own check. Their names have no dot, so they never collide with an operationId.
/// </summary>
internal static class RunnerDispatch
{
    public static readonly IReadOnlyDictionary<string, DispatchEntry> Operations = new Dictionary<string, DispatchEntry>(StringComparer.Ordinal)
    {
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
                ["expiresAt"] = verified.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            };
        }, null),
    };
}
