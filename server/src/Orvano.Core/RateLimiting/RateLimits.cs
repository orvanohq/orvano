using System.Collections.Concurrent;

namespace Orvano.Core.RateLimiting;

/// <summary>A named fixed window limit: at most <paramref name="PermitLimit"/> per key in each <paramref name="Window"/>.</summary>
/// <param name="Name">The policy name, unique.</param>
/// <param name="PermitLimit">How many acquisitions each key gets per window.</param>
/// <param name="Window">The window length.</param>
public sealed record RateLimitPolicy(string Name, int PermitLimit, TimeSpan Window);

/// <summary>
/// The built in limits (spec 0004, rate limits; spec 0006 for the setup status; spec 0014's Rate limits table). The
/// editable ones (spec 0014, AC-21) are the defaults here; a project's own values come in as a copy with its numbers.
/// "Limit IP" is spec 0014's AC-16 address, with the project in the key for app projects.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Password sign in (app and console), every attempt, keyed by project plus limit IP.</summary>
    public static RateLimitPolicy SignInPerIp { get; } = new("auth.sign_in.ip", 300, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Password sign ins answered 401 <c>invalid_credentials</c>, keyed by project, lowercased email, and limit IP;
    /// reserved while in flight (spec 0014, AC-17). Editable: limit and window.
    /// </summary>
    public static RateLimitPolicy FailedSignInPerEmailIp { get; } = new("auth.sign_in_failed.email_ip", 10, TimeSpan.FromMinutes(15));

    /// <summary>Password sign ins answered 401, keyed by project plus limit IP; reserved (spec 0014, AC-17). Editable.</summary>
    public static RateLimitPolicy FailedSignInPerIp { get; } = new("auth.sign_in_failed.ip", 100, TimeSpan.FromMinutes(15));

    /// <summary><c>account.createAnonymousSession</c>, keyed by project plus limit IP (spec 0014, AC-28). Editable.</summary>
    public static RateLimitPolicy AnonymousPerIp { get; } = new("auth.anonymous.ip", 30, TimeSpan.FromHours(1));

    /// <summary><c>account.createAnonymousSession</c>, keyed by project (spec 0014, AC-28).</summary>
    public static RateLimitPolicy AnonymousPerProject { get; } = new("auth.anonymous.project", 1000, TimeSpan.FromHours(1));

    /// <summary>Every auth email, keyed by project, lowercased recipient, and kind, whatever the address (spec 0014, AC-20).</summary>
    public static RateLimitPolicy EmailSendPerRecipientTotal { get; } = new("auth.email_send.recipient_total", 20, TimeSpan.FromHours(1));

    /// <summary>
    /// <c>createEmailCodeSession</c> answered 401 <c>invalid_code</c>, keyed by project, lowercased email, and limit IP;
    /// reserved (spec 0014, AC-19).
    /// </summary>
    public static RateLimitPolicy FailedEmailCodePerRecipientIp { get; } = new("auth.email_code_failed.recipient_ip", 5, TimeSpan.FromMinutes(15));

    /// <summary>Every wrong email code for one email, keyed by project plus lowercased email; reserved (spec 0014, AC-19).</summary>
    public static RateLimitPolicy FailedEmailCodePerRecipient { get; } = new("auth.email_code_failed.recipient", 30, TimeSpan.FromHours(1));

    /// <summary>Wrong TOTP codes, keyed by user ID; reserved. Over it, recovery codes and passkeys still work (spec 0014, AC-18).</summary>
    public static RateLimitPolicy FailedTotpPerUser { get; } = new("auth.mfa_totp_failed.user", 60, TimeSpan.FromHours(1));

    /// <summary>
    /// Requests whose API key fails, keyed by connection IP, install wide. Over it a failing key gets 429; a valid key
    /// always passes (spec 0014, AC-22).
    /// </summary>
    public static RateLimitPolicy FailedApiKeyPerIp { get; } = new("auth.api_key_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary>Every console invitation email (create and resend), keyed by lowercased recipient (spec 0014, AC-23).</summary>
    public static RateLimitPolicy ConsoleInviteEmailPerRecipient { get; } = new("console.invite_email.recipient", 5, TimeSpan.FromHours(1));

    /// <summary>
    /// <c>account.create</c>, <c>users.create</c>, <c>consoleAccount.create</c>, and a magic link or email code that
    /// creates a user (spec 0010, AC-15), keyed by connection IP.
    /// </summary>
    public static RateLimitPolicy SignUpPerIp { get; } = new("auth.sign_up.ip", 60, TimeSpan.FromHours(1));

    /// <summary><c>account.updatePassword</c>, <c>account.delete</c>, and <c>account.updateEmail</c>, keyed by user ID.</summary>
    public static RateLimitPolicy PasswordCheckPerUser { get; } = new("auth.password_check.user", 10, TimeSpan.FromMinutes(15));

    /// <summary><c>account.refreshSession</c> and <c>consoleAccount.refreshSession</c>, keyed by session ID.</summary>
    public static RateLimitPolicy RefreshPerSession { get; } = new("auth.refresh.session", 60, TimeSpan.FromMinutes(15));

    /// <summary>Refreshes answered 401, keyed by connection IP. Checked before, counted only after a 401.</summary>
    public static RateLimitPolicy FailedRefreshPerIp { get; } = new("auth.refresh_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary>
    /// The open email sends (<c>account.createRecovery</c>, <c>createMagicLink</c>, <c>createEmailCode</c>), keyed by
    /// connection IP (spec 0010, AC-7). High, since one app server may send for all its users.
    /// </summary>
    public static RateLimitPolicy EmailSendPerIp { get; } = new("auth.email_send.ip", 300, TimeSpan.FromHours(1));

    /// <summary>Every auth email, keyed by project, lowercased recipient, kind, and limit IP: one a minute (spec 0014, AC-20).</summary>
    public static RateLimitPolicy EmailSendPerRecipientShort { get; } = new("auth.email_send.recipient_short", 1, TimeSpan.FromSeconds(60));

    /// <summary>Every auth email, keyed by project, lowercased recipient, kind, and limit IP: five an hour (spec 0014, AC-20).</summary>
    public static RateLimitPolicy EmailSendPerRecipient { get; } = new("auth.email_send.recipient", 5, TimeSpan.FromHours(1));

    /// <summary>
    /// The five email token redemptions answered 401, keyed by connection IP. Checked before, counted only after a 401
    /// (spec 0010, AC-28).
    /// </summary>
    public static RateLimitPolicy FailedEmailRedeemPerIp { get; } = new("auth.email_redeem_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary><c>account.createOAuthFlow</c> and <c>account.createOAuthLinkFlow</c>, keyed by connection IP (spec 0012, AC-4).</summary>
    public static RateLimitPolicy OAuthStartPerIp { get; } = new("auth.oauth_start.ip", 300, TimeSpan.FromMinutes(15));

    /// <summary>Both OAuth callback routes, keyed by connection IP (spec 0012, rate limits).</summary>
    public static RateLimitPolicy OAuthCallbackPerIp { get; } = new("auth.oauth_callback.ip", 300, TimeSpan.FromMinutes(15));

    /// <summary>
    /// The OAuth code and ID token redemptions answered 401, keyed by connection IP. Checked first, counted only after a
    /// 401, so over it even a valid request is refused (spec 0012, AC-7, AC-9).
    /// </summary>
    public static RateLimitPolicy FailedOAuthRedeemPerIp { get; } = new("auth.oauth_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Wrong second factors in <c>createMfaSession</c>, <c>verifyMfa</c>, <c>confirmTotp</c>, the enrollment
    /// operations, and their console twins, keyed by user ID plus limit IP; reserved (spec 0014, AC-18).
    /// </summary>
    public static RateLimitPolicy FailedMfaPerUserIp { get; } = new("auth.mfa_failed.user_ip", 10, TimeSpan.FromMinutes(15));

    /// <summary>
    /// <c>createMfaSession</c> and <c>createMfaPasskeyChallenge</c> with an unknown or expired ticket, keyed by
    /// connection IP. Checked first, counted only after an <c>invalid_mfa_ticket</c> (spec 0013, rate limits).
    /// </summary>
    public static RateLimitPolicy FailedMfaTicketPerIp { get; } = new("auth.mfa_ticket_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary>The passkey ceremonies' challenges and passkey sign in, keyed by connection IP (spec 0013, rate limits).</summary>
    public static RateLimitPolicy PasskeyPerIp { get; } = new("auth.passkey.ip", 300, TimeSpan.FromMinutes(15));

    /// <summary>Passkey challenges tied to a user (step two, step up, registration), keyed by user ID (spec 0013, rate limits).</summary>
    public static RateLimitPolicy PasskeyChallengePerUser { get; } = new("auth.passkey_challenge.user", 30, TimeSpan.FromMinutes(15));

    /// <summary>Failed passkey sign ins, keyed by connection IP (spec 0013, rate limits).</summary>
    public static RateLimitPolicy FailedPasskeyPerIp { get; } = new("auth.passkey_failed.ip", 60, TimeSpan.FromMinutes(15));

    /// <summary><c>createTotp</c>, <c>createPasskeyRegistration</c>, and <c>createRecoveryCodes</c>, keyed by user ID (spec 0013, rate limits).</summary>
    public static RateLimitPolicy MfaEnrollPerUser { get; } = new("auth.mfa_enroll.user", 10, TimeSpan.FromMinutes(15));

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
/// keys (an email, a session ID) are only known once the body is read, and some limits count only failures. Each
/// policy (with its numbers, so a project that changes an editable value starts fresh counters) keeps at most
/// <see cref="MaxKeysPerPolicy"/> keys, dropping the oldest windows first, because keys include attacker chosen emails
/// (spec 0014). A second <c>api</c> instance needs Valkey first, or its limits double.
/// </summary>
public sealed class RateLimits : IDisposable
{
    /// <summary>The most keys one policy remembers.</summary>
    public const int DefaultMaxKeysPerPolicy = 100_000;

    private readonly ConcurrentDictionary<RateLimitPolicy, WindowStore> _stores = new();
    private readonly TimeProvider _time;
    private readonly int _maxKeys;

    /// <summary>Limits on the system clock with the default key cap.</summary>
    public RateLimits()
        : this(TimeProvider.System, DefaultMaxKeysPerPolicy)
    {
    }

    /// <summary>Limits on <paramref name="time"/>, remembering at most <paramref name="maxKeysPerPolicy"/> keys per policy.</summary>
    public RateLimits(TimeProvider time, int maxKeysPerPolicy)
    {
        _time = time;
        _maxKeys = maxKeysPerPolicy;
    }

    /// <summary>The key cap of each policy.</summary>
    public int MaxKeysPerPolicy => _maxKeys;

    /// <summary>Takes one permit for <paramref name="key"/>, or refuses when the window's permits are spent.</summary>
    public RateLimitDecision Acquire(RateLimitPolicy policy, string key) => Store(policy).Take(key);

    /// <summary>Whether <paramref name="key"/> has a permit left (counting reservations in flight), without taking one.</summary>
    public RateLimitDecision Check(RateLimitPolicy policy, string key) => Store(policy).Peek(key);

    /// <summary>
    /// Reserves one slot under every limit at once (spec 0014, AC-17): a reservation counts toward its limit while the
    /// attempt is in flight, so parallel attempts can't pass a check together. Refused when any limit is full, and then
    /// nothing stays reserved. Count the failure with <see cref="FailureReservation.Fail(RateLimitPolicy[])"/>;
    /// disposing releases whatever was not counted.
    /// </summary>
    public FailureReservation Reserve(params ReadOnlySpan<(RateLimitPolicy Policy, string Key)> limits)
    {
        var held = new List<(WindowStore Store, RateLimitPolicy Policy, string Key)>(limits.Length);
        foreach (var (policy, key) in limits)
        {
            var store = Store(policy);
            var decision = store.TryReserve(key);
            if (!decision.Allowed)
            {
                foreach (var (taken, _, takenKey) in held) taken.Settle(takenKey, failed: false);
                return new FailureReservation([], decision.RetryAfter);
            }

            held.Add((store, policy, key));
        }

        return new FailureReservation(held, null);
    }

    /// <summary>How many keys a policy remembers now, for tests of the cap.</summary>
    public int KeyCount(RateLimitPolicy policy) => _stores.TryGetValue(policy, out var store) ? store.Count : 0;

    private WindowStore Store(RateLimitPolicy policy) => _stores.GetOrAdd(policy, p => new WindowStore(p, _time, _maxKeys));

    /// <inheritdoc />
    public void Dispose() => _stores.Clear();
}

/// <summary>
/// Slots reserved by <see cref="RateLimits.Reserve"/> (spec 0014, AC-17). <see cref="Allowed"/> false means a limit was
/// full and nothing is held. Each slot becomes a failure when <see cref="Fail(RateLimitPolicy[])"/> names its policy
/// (or names none), and is released on dispose otherwise.
/// </summary>
public sealed class FailureReservation : IDisposable
{
    private readonly List<(WindowStore Store, RateLimitPolicy Policy, string Key)> _held;
    private readonly TimeSpan? _refusedFor;

    internal FailureReservation(List<(WindowStore Store, RateLimitPolicy Policy, string Key)> held, TimeSpan? refusedFor)
    {
        _held = held;
        _refusedFor = refusedFor;
    }

    /// <summary>Whether every limit had room; when false, answer 429 with <see cref="Decision"/>'s <c>Retry-After</c>.</summary>
    public bool Allowed => _refusedFor is null;

    /// <summary>The decision to answer with: allowed, or refused until the fullest window resets.</summary>
    public RateLimitDecision Decision => new(Allowed, _refusedFor ?? TimeSpan.Zero);

    /// <summary>Counts the attempt as a failure under the given policies, or under every reserved one when none are given.</summary>
    public void Fail(params RateLimitPolicy[] policies)
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var (store, policy, key) = _held[i];
            if (policies.Length > 0 && !policies.Any(p => p.Name == policy.Name)) continue;
            store.Settle(key, failed: true);
            _held.RemoveAt(i);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (store, _, key) in _held) store.Settle(key, failed: false);
        _held.Clear();
    }
}

/// <summary>
/// One policy's fixed windows: per key, a window that starts at its first use, the permits used in it, and the slots
/// reserved by attempts in flight. Keys beyond the cap drop the oldest windows first; expired windows are swept at most
/// once per window length.
/// </summary>
internal sealed class WindowStore(RateLimitPolicy policy, TimeProvider time, int maxKeys)
{
    private sealed class Window
    {
        public DateTimeOffset Start;
        public int Used;
        public int Reserved;
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Lock _evicting = new();
    // UTC ticks of the last sweep, read without the lock, so kept as one atomic value.
    private long _lastSweep = time.GetUtcNow().UtcTicks;

    public int Count => _windows.Count;

    public RateLimitDecision Take(string key) => Apply(key, w => w.Used + w.Reserved < policy.PermitLimit, w => w.Used++);

    public RateLimitDecision TryReserve(string key) => Apply(key, w => w.Used + w.Reserved < policy.PermitLimit, w => w.Reserved++);

    public RateLimitDecision Peek(string key) => Apply(key, w => w.Used + w.Reserved < policy.PermitLimit, _ => { });

    /// <summary>Ends a reservation: it becomes a used permit when <paramref name="failed"/>, else it frees its slot.</summary>
    public void Settle(string key, bool failed)
    {
        var window = Get(key);
        lock (window)
        {
            Roll(window, time.GetUtcNow());
            if (window.Reserved > 0) window.Reserved--;
            if (failed) window.Used++;
        }
    }

    private RateLimitDecision Apply(string key, Func<Window, bool> allowed, Action<Window> take)
    {
        var window = Get(key);
        var now = time.GetUtcNow();
        lock (window)
        {
            Roll(window, now);
            if (!allowed(window))
            {
                RateLimitTelemetry.RecordRefused(policy.Name);
                return new RateLimitDecision(false, window.Start + policy.Window - now);
            }

            take(window);
            return new RateLimitDecision(true, TimeSpan.Zero);
        }
    }

    // A window past its end starts again now; reservations still in flight carry over, since they settle later.
    private void Roll(Window window, DateTimeOffset now)
    {
        if (now < window.Start + policy.Window) return;
        window.Start = now;
        window.Used = 0;
    }

    private Window Get(string key)
    {
        if (_windows.TryGetValue(key, out var found)) return found;
        MakeRoom();
        return _windows.GetOrAdd(key, _ => new Window { Start = time.GetUtcNow() });
    }

    private void MakeRoom()
    {
        var now = time.GetUtcNow();
        if (_windows.Count < maxKeys && now.UtcTicks - Interlocked.Read(ref _lastSweep) < policy.Window.Ticks) return;
        lock (_evicting)
        {
            if (now.UtcTicks - Interlocked.Read(ref _lastSweep) >= policy.Window.Ticks || _windows.Count >= maxKeys)
            {
                Interlocked.Exchange(ref _lastSweep, now.UtcTicks);
                foreach (var (key, window) in _windows)
                {
                    if (window.Reserved == 0 && now >= window.Start + policy.Window) _windows.TryRemove(key, out _);
                }
            }

            if (_windows.Count < maxKeys) return;

            // Still full of live windows: drop the oldest tenth at once, so the cost is paid once per many new keys.
            var drop = Math.Max(1, maxKeys / 10);
            foreach (var (key, _) in _windows.OrderBy(entry => entry.Value.Start).Take(drop)) _windows.TryRemove(key, out _);
        }
    }
}
