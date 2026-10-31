namespace Orvano.Modules.Auth.Contracts;

/// <summary>The rules a new password must pass (spec 0017 PSW-1, spec 0006 PWD-3, PWD-4). Feature 14 reuses it.</summary>
public interface IPasswordPolicy
{
    /// <summary>
    /// The field code that refuses <paramref name="password"/>: <c>too_short</c>, <c>too_long</c>, or
    /// <c>password_too_common</c>. Null when it passes.
    /// </summary>
    string? Check(string password);
}
