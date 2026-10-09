using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Orvano.Contract;
using Orvano.Core.Modules;
using Orvano.Server.Hosting;

namespace Orvano.Server.Modules;

/// <summary>
/// The test only operations (spec 0001, AC-18) with fixed answers, so the scenarios can prove the
/// SDK conventions: errors, pagination, and the console audience. <see cref="OrvanoModules"/> adds
/// it only in the <c>Test</c> environment; anywhere else these routes do not exist (404).
/// </summary>
internal sealed class TestingModule : IOrvanoModule
{
    private const int DefaultLimit = 2;
    private const int MaxLimit = 100;

    private static readonly TestItem[] Items = [.. Enumerable.Range(1, 5).Select(i => new TestItem($"item-{i}"))];

    public string Name => "testing";

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<TestEmails>();
        services.AddSingleton<FakeOAuthProvider>();
        services.AddSingleton<SoftwareAuthenticator>();
    }

    public void MapApi(RouteGroupBuilder v1)
    {
        v1.MapPost(TestOperations.Conflict.Route, () =>
                Problems.Result(StatusCodes.Status409Conflict, TestErrorCode.TestConflict, "This test operation always conflicts."))
            .WithName(TestOperations.Conflict.Id);

        v1.MapGet(TestOperations.List.Route, List).WithName(TestOperations.List.Id);

        v1.MapGet(TestOperations.GetLatestEmail.Route, async Task<Results<Ok<TestEmail>, ProblemHttpResult>> (
                string? to, DateTimeOffset? after, string? subject, TestEmails emails, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(to)
                ? Problems.Result(StatusCodes.Status400BadRequest, ErrorCode.InvalidRequest, "Send the recipient as to.")
                : await emails.FindLatestAsync(to, after, subject, ct) is { } email
                    ? TypedResults.Ok(email)
                    : Problems.Result(StatusCodes.Status404NotFound, ErrorCode.NotFound, "No email to that address arrived within 15 seconds."))
            .WithName(TestOperations.GetLatestEmail.Id);

        v1.MapGet(TestOperations.ConsolePing.Route, () => TypedResults.Ok(new TestConsolePing("ok")))
            .WithName(TestOperations.ConsolePing.Id);

        var fake = ((IEndpointRouteBuilder)v1).ServiceProvider.GetRequiredService<FakeOAuthProvider>();
        fake.Map(v1);

        v1.MapPost(TestOperations.CreateIdToken.Route, Results<Ok<TestIdToken>, ProblemHttpResult> (TestCreateIdTokenRequest request) =>
        {
            var provider = request.Provider is "google" or "apple" ? request.Provider : null;
            if (provider is null) return Problems.Result(StatusCodes.Status400BadRequest, ErrorCode.InvalidRequest, "The provider must be google or apple.");
            var user = new FakeOAuthProvider.TestUser(request.Sub, request.Email, request.EmailVerified, null, null, null, null, null, null);
            var (idToken, code) = fake.MintNative(provider, request.Aud, user, request.Nonce, request.ExpiresIn is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
            return TypedResults.Ok(new TestIdToken(idToken, code));
        })
            .WithName(TestOperations.CreateIdToken.Id);

        v1.MapPost(TestOperations.CreatePasskeyCredential.Route, (TestCreatePasskeyCredentialRequest request, SoftwareAuthenticator authenticator) =>
            TypedResults.Ok(authenticator.Create(request)))
            .WithName(TestOperations.CreatePasskeyCredential.Id);

        v1.MapPost(TestOperations.CreatePasskeyAssertion.Route, Results<Ok<PasskeyAssertionCredential>, ProblemHttpResult> (
                TestCreatePasskeyAssertionRequest request, SoftwareAuthenticator authenticator) =>
            authenticator.Assert(request) is { } assertion
                ? TypedResults.Ok(assertion)
                : Problems.Result(StatusCodes.Status404NotFound, ErrorCode.NotFound, "This authenticator made no passkey with that ID in this run."))
            .WithName(TestOperations.CreatePasskeyAssertion.Id);

        v1.MapGet(TestOperations.ListAppleRevocations.Route, (DateTimeOffset? after) =>
            TypedResults.Ok(new TestAppleRevocationList([.. fake.Revocations
                .Where(r => after is null || r.ReceivedAt > after)
                .Select(r => new TestAppleRevocation(r.ClientId, r.TokenHint, r.ReceivedAt))])))
            .WithName(TestOperations.ListAppleRevocations.Id);
    }

    public void RegisterWork(IWorkRegistry work) { }

    public void RegisterRealtime(IRealtimeRegistry realtime) { }

    private static Results<Ok<TestItemPage>, ProblemHttpResult> List(string? cursor, int? limit)
    {
        var size = limit ?? DefaultLimit;
        if (size is < 1 or > MaxLimit)
            return Problems.Result(StatusCodes.Status400BadRequest, ErrorCode.InvalidRequest, $"limit must be 1 to {MaxLimit}.");

        var offset = 0;
        if (cursor is not null && !TryReadCursor(cursor, out offset))
            return Problems.Result(StatusCodes.Status400BadRequest, ErrorCode.InvalidCursor, "The cursor is not one this server issued.");

        var end = Math.Min(offset + size, Items.Length);
        var next = end < Items.Length ? WriteCursor(end) : null;
        return TypedResults.Ok(new TestItemPage(Items[offset..end], next));
    }

    /// <summary>The next offset as a base64url encoded decimal: opaque to SDKs.</summary>
    private static string WriteCursor(int offset) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static bool TryReadCursor(string cursor, out int offset)
    {
        offset = 0;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            return text.All(char.IsAsciiDigit)
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                && offset is >= 0 and <= 5;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
