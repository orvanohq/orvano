using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Platform;

/// <summary>The platform module's error codes (spec 0014 PRB-8), registered with <c>AddErrorCodes</c>.</summary>
public static class PlatformErrorCodes
{
    [ErrorCode(ErrorKind.Unauthorized, "The setup token is wrong, expired, or replaced; make a new one with orvano admin setup-token.")]
    public const string SetupTokenInvalid = "setup_token_invalid";

    [ErrorCode(ErrorKind.Conflict, "Setup is already complete; sign in instead.")]
    public const string SetupComplete = "setup_complete";

    [ErrorCode(ErrorKind.Unauthorized, "The email or password is wrong.")]
    public const string InvalidCredentials = "invalid_credentials";

    [ErrorCode(ErrorKind.Forbidden, "This needs a recent password check; confirm the password, then try again.")]
    public const string ReauthRequired = "reauth_required";

    [ErrorCode(ErrorKind.Unauthorized, "The reset link is wrong, expired, used, or replaced; make a new one with orvano admin reset-operator.")]
    public const string ResetTokenInvalid = "reset_token_invalid";

    [ErrorCode(ErrorKind.Conflict, "The project is being created or deleted, so this can't be done now.")]
    public const string ProjectNotActive = "project_not_active";

    [ErrorCode(ErrorKind.Conflict, "The API key is revoked or expired, so it can't be regenerated.")]
    public const string ApiKeyNotActive = "api_key_not_active";
}

/// <summary>The platform errors used in more than one place.</summary>
public static class PlatformErrors
{
    /// <summary>Spec 0017 SES-8.</summary>
    public static Error ReauthRequired { get; } =
        Error.Forbidden(PlatformErrorCodes.ReauthRequired, "This needs a recent password check. Confirm your password, then try again.");

    internal static Error ProjectNotActive { get; } =
        Error.Conflict(PlatformErrorCodes.ProjectNotActive, "The project is being created or deleted, so this can't be done now.");
}
