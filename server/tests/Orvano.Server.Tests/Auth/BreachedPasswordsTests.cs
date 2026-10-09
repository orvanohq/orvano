using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0014, AC-5 to AC-7 as plain code: the bundled lists, and the breached check's request and answer rules, with an
// in process HTTP handler standing in for the range API.
public class BreachedPasswordsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_bundled_lists_load_and_match_as_ac_5_and_ac_8_say()
    {
        var (common, disposable) = BundledLists.Counts;
        Assert.InRange(common, 10_000, 100_000);
        Assert.InRange(disposable, 1_000, 100_000);

        Assert.True(BundledLists.IsCommonPassword("password123"));
        Assert.True(BundledLists.IsCommonPassword("PASSWORD123"));
        Assert.False(BundledLists.IsCommonPassword("correct horse battery staple"));

        Assert.True(BundledLists.IsDisposableDomain("mailinator.com"));
        Assert.True(BundledLists.IsDisposableDomain("inbox.mailinator.com"));
        Assert.False(BundledLists.IsDisposableDomain("example.com"));
    }

    [Fact]
    public void A_listed_suffix_counts_only_with_a_count_above_zero()
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("hunter2 hunter2")));
        var suffix = Encoding.ASCII.GetBytes(hash[5..]);
        var other = new string('A', 35);

        Assert.True(BreachedPasswords.Lists($"{other}:4\r\n{hash[5..]}:12\r\n", suffix));
        Assert.True(BreachedPasswords.Lists($"{hash[5..].ToLowerInvariant()}:1", suffix));
        Assert.False(BreachedPasswords.Lists($"{hash[5..]}:0\r\n{other}:9", suffix)); // a padding line
        Assert.False(BreachedPasswords.Lists($"{other}:3", suffix));
        Assert.False(BreachedPasswords.Lists("not a range body", suffix));
    }

    [Fact]
    public async Task The_request_carries_only_the_prefix_and_padding_and_a_hit_is_breached()
    {
        var password = "hunter2 hunter2";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{hash[5..]}:7\r\n") });

        Assert.True(await Check(handler).IsBreachedAsync(password, Ct));
        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://range.test/range/{hash[..5]}", request.RequestUri!.AbsoluteUri);
        Assert.Equal("true", request.Headers.GetValues("Add-Padding").Single());
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task Any_failure_passes_the_password_and_counts_a_failure()
    {
        var failures = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "orvano.auth.hibp_failures") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref failures, value));
        listener.Start();

        Assert.False(await Check(new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))).IsBreachedAsync("hunter2 hunter2", Ct));
        Assert.False(await Check(new Handler(_ => throw new HttpRequestException("down"))).IsBreachedAsync("hunter2 hunter2", Ct));
        Assert.False(await Check(new Handler(_ => throw new TaskCanceledException("timeout"))).IsBreachedAsync("hunter2 hunter2", Ct));
        Assert.True(Interlocked.Read(ref failures) >= 3);
    }

    private static BreachedPasswords Check(Handler handler) => new(new Factory(handler), new Uri("https://range.test/"));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
