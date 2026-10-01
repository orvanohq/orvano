using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Orvano.Core.RateLimiting;

/// <summary>A named fixed window limit: at most <paramref name="PermitLimit"/> per key in each <paramref name="Window"/>.</summary>
/// <param name="Name">The policy name, unique.</param>
/// <param name="PermitLimit">How many acquisitions each key gets per window.</param>
/// <param name="Window">The window length.</param>
public sealed record RateLimitPolicy(string Name, int PermitLimit, TimeSpan Window);

/// <summary>The built in limits (spec 0004, rate limits; spec 0006 for the setup status). Row 14 makes them per project settings.</summary>
public static class RateLimitPolicies
{
    /// <summary>Password sign in (app and console), keyed by project plus lowercased email.</summary>
    public static RateLimitPolicy SignInPerEmail { get; } = new("auth.sign_in.email", 10, TimeSpan.FromMinutes(15));

    /// <summary>Password sign in (app and console), keyed by connection IP.</summary>
    public static RateLimitPolicy SignInPerIp { get; } = new("auth.sign_in.ip", 300, TimeSpan.FromMinutes(15));

    /// <summary><c>account.create</c>, <c>users.create</c>, and <c>consoleAccount.create</c>, keyed by connection IP.</summary>
    public static RateLimitPolicy SignUpPerIp { get; } = new("auth.sign_up.ip", 60, TimeSpan.FromHours(1));

    /// <summary><c>account.updatePassword</c> and <c>account.delete</c>, keyed by user ID.</summary>
    public static RateLimitPolicy PasswordCheckPerUser { get; } = new("auth.password_check.user", 10, TimeSpan.FromMinutes(15));

    /// <summary><c>account.refreshSession</c> and <c>consoleAccount.refreshSession</c>, keyed by session ID.</summary>
    public static RateLimitPolicy RefreshPerSession { get; } = new("auth.refresh.session", 60, TimeSpan.FromMinutes(15));

    /// <summary>Refreshes answered 401, keyed by connection IP. Checked before, counted only after a 401.</summary>
    public static RateLimitPolicy FailedRefreshPerIp { get; } = new("auth.refresh_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary><c>consoleInstall.getSetup</c>, keyed by connection IP (spec 0006, AC-22).</summary>
    public static RateLimitPolicy ConsoleSetupPerIp { get; } = new("console.setup.ip", 60, TimeSpan.FromMinutes(1));

    /// <summary><c>consoleInvitations.create</c>, keyed by console user ID; every attempt counts (spec 0008, AC-1).</summary>
    public static RateLimitPolicy ConsoleInviteCreatePerUser { get; } = new("console.invite_create.user", 60, TimeSpan.FromHours(1));

    /// <summary><c>consoleInvitations.preview</c>, keyed by connection IP; every attempt counts (spec 0008, AC-5).</summary>
    public static RateLimitPolicy ConsoleInvitePreviewPerIp { get; } = new("console.invite_preview.ip", 60, TimeSpan.FromMinutes(1));

    /// <summary><c>consoleInvitations.accept</c>, keyed by console user ID; every attempt counts (spec 0008, AC-6).</summary>
    public static RateLimitPolicy ConsoleInviteAcceptPerUser { get; } = new("console.invite_accept.user", 30, TimeSpan.FromMinutes(15));

    /// <summary>Every test email (<c>consoleSmtp.test</c> and its siblings), keyed by console user ID; every attempt that passed the role check counts (spec 0009, AC-25).</summary>
    public static RateLimitPolicy MessagingTestPerUser { get; } = new("messaging.test.user", 30, TimeSpan.FromMinutes(15));

    /// <summary>Every template preview (<c>consoleEmailTemplates.preview</c>), keyed by console user ID; every attempt that passed the role check counts (spec 0009, AC-25).</summary>
    public static RateLimitPolicy MessagingPreviewPerUser { get; } = new("messaging.preview.user", 300, TimeSpan.FromMinutes(5));
}

/// <summary>The answer of a limit check.</summary>
/// <param name="Allowed">Whether the call may go ahead.</param>
/// <param name="RetryAfter">When refused, how long until the window resets.</param>
public readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
    /// <summary>The <c>Retry-After</c> header value: whole seconds, rounded up, at least 1.</summary>
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}

/// <summary>
/// In memory fixed window limits, per <c>api</c> process (spec 0002). Endpoints call them explicitly, since several
/// keys (an email, a session ID) are only known once the body is read, and one limit counts only failures. A second
/// <c>api</c> instance needs Valkey first, or its limits double.
/// </summary>
public sealed class RateLimits : IDisposable
{
    private readonly ConcurrentDictionary<string, PartitionedRateLimiter<string>> _limiters = new(StringComparer.Ordinal);

    /// <summary>Takes one permit for <paramref name="key"/>, or refuses when the window's permits are spent.</summary>
    public RateLimitDecision Acquire(RateLimitPolicy policy, string key) => Attempt(policy, key, 1);

    /// <summary>Whether <paramref name="key"/> has a permit left, without taking one.</summary>
    public RateLimitDecision Check(RateLimitPolicy policy, string key) => Attempt(policy, key, 0);

    private RateLimitDecision Attempt(RateLimitPolicy policy, string key, int permits)
    {
        var limiter = _limiters.GetOrAdd(policy.Name, _ => Create(policy));
        using var lease = limiter.AttemptAcquire(key, permits);
        if (lease.IsAcquired) return new RateLimitDecision(true, TimeSpan.Zero);
        return new RateLimitDecision(false, lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : policy.Window);
    }

    private static PartitionedRateLimiter<string> Create(RateLimitPolicy policy) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = policy.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var limiter in _limiters.Values) limiter.Dispose();
        _limiters.Clear();
    }
}
