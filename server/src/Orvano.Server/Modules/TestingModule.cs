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

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public void MapApi(RouteGroupBuilder v1)
    {
        v1.MapPost(TestOperations.Conflict.Route, () =>
                Problems.Result(StatusCodes.Status409Conflict, TestErrorCode.TestConflict, "This test operation always conflicts."))
            .WithName(TestOperations.Conflict.Id);

        v1.MapGet(TestOperations.List.Route, List).WithName(TestOperations.List.Id);

        v1.MapGet(TestOperations.ConsolePing.Route, () => TypedResults.Ok(new TestConsolePing("ok")))
            .WithName(TestOperations.ConsolePing.Id);
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
