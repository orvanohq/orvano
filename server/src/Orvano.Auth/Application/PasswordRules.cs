using Orvano.Auth.Domain;

namespace Orvano.Auth.Application;

/// <summary>
/// The checks on a new password (spec 0014, AC-4), in order: spec 0004's floor (NFKC, 8 to 256 code points), the
/// project's <c>passwordMinLength</c> in code points after NFKC, the common list (AC-5), and the breached check (AC-6).
/// Every operation that sets a password calls it; a password checked at sign in, <c>account.delete</c>, or step up
/// never does, so tightening the rules never refuses a password someone already has. It may call the range API, so
/// callers run it before any transaction or lock.
/// </summary>
internal sealed class PasswordRules(PolicySettings settings, BreachedPasswords breached)
{
    /// <summary>The password's NFKC form when it passes every rule of <paramref name="projectId"/>, else the refusal.</summary>
    public async Task<Outcome<string>> CheckNewAsync(string projectId, string? password, CancellationToken ct)
    {
        var policies = (await settings.GetAsync(projectId, ct)).Auth;
        if (!PasswordPolicy.TryNormalize(password, out var normalized) || PasswordPolicy.CodePoints(normalized) < policies.PasswordMinLength)
            return Failure.PasswordTooShort(policies.PasswordMinLength);
        if (policies.PasswordCommonCheck && BundledLists.IsCommonPassword(normalized)) return Failure.PasswordTooCommon;
        if (policies.PasswordBreachedCheck && await breached.IsBreachedAsync(normalized, ct)) return Failure.PasswordBreached;
        return normalized;
    }
}
