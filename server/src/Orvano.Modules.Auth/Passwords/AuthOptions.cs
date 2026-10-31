using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace Orvano.Modules.Auth.Passwords;

/// <summary>The Auth module's settings (spec 0002 CFG-5).</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>
    /// How many password hashes run at once (spec 0006 PWD-4). Unset, it is the larger of 2 and a quarter of the memory
    /// the process may use, divided by the Argon2 memory cost.
    /// </summary>
    [Range(1, 1024)]
    public int? HashConcurrency { get; init; }
}

[OptionsValidator]
internal sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>;
