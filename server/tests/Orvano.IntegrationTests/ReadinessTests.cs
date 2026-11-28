using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Orvano.IntegrationTests;

/// <summary>
/// <c>/readyz</c> follows Postgres while <c>/healthz</c> and the process stay up (spec 0002, AC-3, AC-5).
/// The container is paused, not stopped, because a restarted container gets a new host port.
/// </summary>
public sealed class ReadinessTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncDisposable
{
    private static readonly TimeSpan NotReadyWithin = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReadyAgainWithin = TimeSpan.FromSeconds(5);

    // Room for the test's own HTTP round trip and polling, on top of the limits above.
    private static readonly TimeSpan Measurement = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly OrvanoAppFactory _factory = new(postgres.ConnectionString);

    [Fact]
    public async Task Readyz_turns_unavailable_while_postgres_is_unreachable_and_recovers_without_a_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        Assert.Equal((HttpStatusCode.OK, "ok"), await GetStatusAsync(client, "/readyz", cancellationToken));

        await postgres.Container.PauseAsync(cancellationToken);
        try
        {
            var notReadyAfter = await PollUntilAsync(client, HttpStatusCode.ServiceUnavailable, NotReadyWithin + Measurement, cancellationToken);
            Assert.True(notReadyAfter <= NotReadyWithin + Measurement, $"/readyz took {notReadyAfter.TotalSeconds:0.00}s to turn 503.");

            Assert.Equal((HttpStatusCode.ServiceUnavailable, "unavailable"), await GetStatusAsync(client, "/readyz", cancellationToken));
            Assert.Equal((HttpStatusCode.OK, "ok"), await GetStatusAsync(client, "/healthz", cancellationToken));
        }
        finally
        {
            await postgres.Container.UnpauseAsync(CancellationToken.None);
        }

        var readyAfter = await PollUntilAsync(client, HttpStatusCode.OK, ReadyAgainWithin + Measurement, cancellationToken);
        Assert.True(readyAfter <= ReadyAgainWithin + Measurement, $"/readyz took {readyAfter.TotalSeconds:0.00}s to turn 200 again.");
        Assert.Equal((HttpStatusCode.OK, "ok"), await GetStatusAsync(client, "/readyz", cancellationToken));
    }

    private static async Task<TimeSpan> PollUntilAsync(HttpClient client, HttpStatusCode expected, TimeSpan deadline, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var (status, _) = await GetStatusAsync(client, "/readyz", cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (status == expected || elapsed > deadline)
            {
                return elapsed;
            }

            await Task.Delay(PollEvery, cancellationToken);
        }
    }

    private static async Task<(HttpStatusCode Status, string? Value)> GetStatusAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        return (response.StatusCode, json.RootElement.GetProperty("status").GetString());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
