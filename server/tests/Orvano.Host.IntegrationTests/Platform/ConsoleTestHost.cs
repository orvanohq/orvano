using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

/// <summary>The console's session, as the browser holds it. Tests carry the cookie by hand, since ManualClock's dates are in the past.</summary>
internal sealed record ConsoleSession(string Cookie, string CsrfToken, Guid OperatorId, Guid OrganizationId);

/// <summary>
/// A real host on the test's own migrated database (spec 0002 TST-6), for spec 0017's console tests: a manual clock, a
/// counting hasher, a recording audit sink, and the request shapes the console sends.
/// </summary>
internal sealed partial class ConsoleTestHost : IDisposable
{
    public const string PublicUrl = "http://localhost:8080";
    public const string OwnerEmail = "owner@example.com";
    public const string OwnerPhrase = "correct horse battery staple";

    private readonly ConsoleBuild _console = new();
    private readonly OrvanoApiFactory _factory;

    public ConsoleTestHost(
        PostgresFixture postgres,
        string database,
        ManualClock? clock = null,
        Dictionary<string, string>? settings = null,
        Action<IServiceCollection>? services = null)
    {
        Clock = clock ?? new ManualClock();
        Dictionary<string, string> all = OrvanoApiFactory.DefaultSettings(postgres);
        all["Database:Name"] = database;
        all["PUBLIC_URL"] = PublicUrl;
        foreach ((string key, string value) in settings ?? [])
        {
            all[key] = value;
        }

        _factory = new OrvanoApiFactory(all, _console.ContentRoot, collection =>
        {
            collection.AddSingleton<TimeProvider>(Clock);
            collection.AddSingleton(new SetupOutput(TextWriter.Null));
            collection.AddSingleton<IPasswordHasher>(Hasher);
            collection.AddSingleton<IAuditSink>(Audit);
            collection.AddSingleton<ILoggerProvider>(Logs);
            services?.Invoke(collection);
        });
        Client = _factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ManualClock Clock { get; }

    public CountingHasher Hasher { get; } = new();

    public RecordingAuditSink Audit { get; } = new();

    public RecordedLogs Logs { get; } = new();

    public HttpClient Client { get; }

    public IServiceProvider Services => _factory.Services;

    public async Task<string> IssueSetupTokenAsync()
    {
        SetupLink link = (await PlatformSetup.IssueLinkAsync(_factory.Services, Token))!;
        return link.Url[(link.Url.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..];
    }

    // The bodies say `password`; the variables don't, since these are throwaway test values (CodeQL cs/sensitive-data-transmission).
    public static string OwnerBody(string token, string phrase = OwnerPhrase, string email = OwnerEmail) =>
        JsonSerializer.Serialize(new { token, name = "  Ada Owner ", email, password = phrase, organizationName = " Acme " });

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        ConsoleSession? session = null,
        string? origin = PublicUrl,
        string? csrf = null,
        string? key = null,
        string? authorization = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (session is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);
        }

        if ((csrf ?? session?.CsrfToken) is { } token)
        {
            request.Headers.TryAddWithoutValidation(OrvanoHeaders.Csrf, token);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(OrvanoHeaders.Key, key);
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await Client.SendAsync(request, Token);
    }

    public Task<HttpResponseMessage> PostAsync(string path, string body, ConsoleSession? session = null) =>
        SendAsync(HttpMethod.Post, path, body, session);

    public Task<HttpResponseMessage> GetAsync(string path, ConsoleSession? session = null, string? key = null, string? origin = null) =>
        SendAsync(HttpMethod.Get, path, session: session, origin: origin, key: key);

    public async Task<ConsoleSession> CreateOwnerAsync()
    {
        using HttpResponseMessage response = await PostAsync("/v1/console/setup/owner", OwnerBody(await IssueSetupTokenAsync()));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadSessionAsync(response);
    }

    public async Task<ConsoleSession> SignInAsync(string email = OwnerEmail, string phrase = OwnerPhrase)
    {
        using HttpResponseMessage response = await PostAsync("/v1/console/session", JsonSerializer.Serialize(new { email, password = phrase }));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadSessionAsync(response);
    }

    public async Task<Guid> CreateProjectAsync(ConsoleSession session, string name = "Shop", Guid? organizationId = null)
    {
        using HttpResponseMessage response = await PostAsync(
            $"/v1/console/organizations/{organizationId ?? session.OrganizationId}/projects", JsonSerializer.Serialize(new { name }), session);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).RootElement.GetProperty("id").GetGuid();
    }

    public async Task<string> CreateKeyAsync(ConsoleSession session, Guid projectId, string type = "secret", string[]? scopes = null)
    {
        using HttpResponseMessage response = await PostAsync(
            $"/v1/console/projects/{projectId}/keys",
            JsonSerializer.Serialize(new { type, name = "Worker", scopes = scopes ?? (type == "secret" ? ["storage.read"] : null) }),
            session);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadJsonAsync(response)).RootElement.GetProperty("key").GetString()!;
    }

    public async Task<T> WithDatabaseAsync<T>(Func<PlatformDbContext, Task<T>> work)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<PlatformDbContext>());
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).RootElement.GetProperty("code").GetString();

    public static async Task<string?> FieldCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).RootElement.GetProperty("errors")[0].GetProperty("code").GetString();

    public static string? SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values.Single() : null;

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        _console.Dispose();
    }

    private static async Task<ConsoleSession> ReadSessionAsync(HttpResponseMessage response)
    {
        JsonElement root = (await ReadJsonAsync(response)).RootElement;
        string cookie = CookiePair().Match(SetCookie(response)!).Value;
        return new ConsoleSession(
            cookie,
            root.GetProperty("csrfToken").GetString()!,
            root.GetProperty("operator").GetProperty("id").GetGuid(),
            root.GetProperty("memberships").EnumerateArray().Select(x => x.GetProperty("organizationId").GetGuid()).FirstOrDefault());
    }

    [GeneratedRegex("^[^;]+")]
    private static partial Regex CookiePair();
}

/// <summary>The real hasher, counted, so a test can prove work was or wasn't done (spec 0017 AC-2, AC-5).</summary>
internal sealed class CountingHasher : IPasswordHasher
{
    private readonly Orvano.Modules.Auth.Passwords.Argon2idPasswordHasher _inner = new();
    private int _hashes;
    private int _verifies;

    public int Hashes => Volatile.Read(ref _hashes);

    public int Verifies => Volatile.Read(ref _verifies);

    public string Hash(string password)
    {
        Interlocked.Increment(ref _hashes);
        return _inner.Hash(password);
    }

    public bool Verify(string password, string hash)
    {
        Interlocked.Increment(ref _verifies);
        return _inner.Verify(password, hash);
    }
}

/// <summary>Every line a host logged.</summary>
internal sealed class RecordedLogs : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");

    public void Dispose()
    {
    }
}

/// <summary>Every audit event a host recorded (spec 0017 AC-22).</summary>
internal sealed class RecordingAuditSink : IAuditSink
{
    private readonly Lock _lock = new();
    private readonly List<AuditEvent> _events = [];

    public bool Fails { get; set; }

    public IReadOnlyList<AuditEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    public ValueTask RecordAsync(AuditEvent audit, CancellationToken cancellationToken)
    {
        if (Fails)
        {
            throw new InvalidOperationException("The sink is down.");
        }

        lock (_lock)
        {
            _events.Add(audit);
        }

        return ValueTask.CompletedTask;
    }
}
