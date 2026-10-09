using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// The fake Have I Been Pwned range API of the <c>Test</c> environment (spec 0014, AC-6), under
/// <c>/v1/test/pwned/range/{prefix}</c>; <c>ORVANO_TEST_HIBP_URL</c> points the breached password check here. It
/// knows three fixed passwords by their SHA-1: <see cref="Breached"/> is listed with a count, <see cref="Slow"/> answers
/// after the check's 2 second timeout, and <see cref="Broken"/> answers 503. Every other prefix gets padding lines
/// only (count 0), as the real API pads. It keeps the last requests it saw (prefix and <c>Add-Padding</c>), which
/// <c>GET /v1/test/pwned/requests</c> answers, so a test can prove only 5 characters left the server. None of these
/// routes is an Orvano operation, so the contract check skips them.
/// </summary>
internal sealed class FakePwnedPasswords
{
    /// <summary>A password the fake lists as breached; not on the common list, at least 12 characters.</summary>
    public const string Breached = "breached horse battery staple";

    /// <summary>A password whose range request answers only after the check gave up.</summary>
    public const string Slow = "slow pwned check password";

    /// <summary>A password whose range request answers 503.</summary>
    public const string Broken = "broken pwned check password";

    private static readonly TimeSpan SlowFor = TimeSpan.FromSeconds(4);
    private const int MaxLog = 1000;

    private readonly ConcurrentQueue<RangeRequest> log = new();

    /// <summary>One request as the fake saw it.</summary>
    public sealed record RangeRequest(string Prefix, string? AddPadding);

    public void Map(RouteGroupBuilder v1)
    {
        v1.MapGet("/test/pwned/range/{prefix}", async (string prefix, HttpRequest request, CancellationToken ct) =>
        {
            log.Enqueue(new RangeRequest(prefix, request.Headers["Add-Padding"].FirstOrDefault()));
            while (log.Count > MaxLog) log.TryDequeue(out _);

            var upper = prefix.ToUpperInvariant();
            if (upper == HashOf(Broken)[..5]) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (upper == HashOf(Slow)[..5]) await Task.Delay(SlowFor, ct);

            var lines = new StringBuilder();
            if (upper == HashOf(Breached)[..5]) lines.Append(HashOf(Breached)[5..]).Append(":3912\r\n");
            // Padding: random suffixes with a count of 0, which a check must never take for a hit.
            for (var i = 0; i < 20; i++)
                lines.Append(Convert.ToHexString(RandomNumberGenerator.GetBytes(18))[..35]).Append(":0\r\n");
            return Results.Text(lines.ToString(), "text/plain");
        }).OutsideContract();

        v1.MapGet("/test/pwned/requests", () => Results.Json(log.ToArray())).OutsideContract();
    }

    private static string HashOf(string password) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC)))).ToUpper(CultureInfo.InvariantCulture);
}
