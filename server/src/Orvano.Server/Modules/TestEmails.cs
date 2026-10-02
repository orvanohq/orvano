using System.Text.Json;
using System.Text.RegularExpressions;
using Orvano.Contract;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// <c>test.getLatestEmail</c> (spec 0010): polls Mailpit's API for up to 15 seconds for the newest message to an
/// address (received after a time, when given) and reads its text part. It leaves the message in Mailpit. Registered
/// only in <c>Test</c>, with the rest of <see cref="TestingModule"/>.
/// </summary>
internal sealed partial class TestEmails(TestMailpit mailpit) : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>The newest matching email, or null when none arrived within 15 seconds (or no Mailpit is set).</summary>
    public async Task<TestEmail?> FindLatestAsync(string to, DateTimeOffset? after, CancellationToken ct)
    {
        if (mailpit.Url is not { } baseUrl) return null;
        var deadline = DateTimeOffset.UtcNow + Wait;
        while (true)
        {
            var search = await _http.GetFromJsonAsync<JsonElement>(
                new Uri(baseUrl, $"/api/v1/search?query={Uri.EscapeDataString($"to:\"{to}\"")}"), ct);
            // Newest first.
            foreach (var summary in search.GetProperty("messages").EnumerateArray())
            {
                if (after is { } since && summary.GetProperty("Created").GetDateTimeOffset() <= since) break;
                var message = await _http.GetFromJsonAsync<JsonElement>(new Uri(baseUrl, $"/api/v1/message/{summary.GetProperty("ID").GetString()}"), ct);
                return Read(message.GetProperty("Subject").GetString() ?? "", message.GetProperty("Text").GetString() ?? "");
            }

            if (DateTimeOffset.UtcNow >= deadline) return null;
            await Task.Delay(Poll, ct);
        }
    }

    /// <summary>The link carrying <c>orvano_token</c>, its type and token, or else the first run of exactly 6 digits.</summary>
    internal static TestEmail Read(string subject, string text)
    {
        foreach (Match candidate in Link().Matches(text))
        {
            if (!Uri.TryCreate(candidate.Value, UriKind.Absolute, out var url) || !candidate.Value.Contains("orvano_token", StringComparison.Ordinal)) continue;
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
            return new TestEmail(subject, query.TryGetValue("orvano_type", out var type) ? type.ToString() : null,
                query.TryGetValue("orvano_token", out var token) ? token.ToString() : null, null, candidate.Value);
        }

        var code = SixDigits().Match(text);
        return new TestEmail(subject, null, null, code.Success ? code.Value : null, null);
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://\S+")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
    private static partial Regex SixDigits();
}
