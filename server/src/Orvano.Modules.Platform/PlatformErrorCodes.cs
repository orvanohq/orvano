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
}
