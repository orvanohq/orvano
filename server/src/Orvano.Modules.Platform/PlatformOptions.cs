using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace Orvano.Modules.Platform;

/// <summary>The console session's limits (spec 0006 CON-3, spec 0017 SES-10).</summary>
public sealed class ConsoleOptions
{
    public const string Section = "Console";

    /// <summary>Hours a session may sit idle.</summary>
    [Range(1, 24 * 365)]
    public int SessionIdleHours { get; init; } = 12;

    /// <summary>Days a session may live, however busy.</summary>
    [Range(1, 365)]
    public int SessionAbsoluteDays { get; init; } = 14;
}

/// <summary>This install, as the console shows it (spec 0017 BOT-1).</summary>
public sealed class InstanceOptions
{
    public const string Section = "Instance";

    /// <summary>The name the console shows.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Name { get; init; } = "Orvano";
}

/// <summary>API key rules an operator may change (spec 0006 KEY-1, spec 0017 AKY-10).</summary>
public sealed class KeysOptions
{
    public const string Section = "Keys";

    /// <summary>Accept a secret key on a request with an <c>Origin</c> header. Off: a browser must never hold one.</summary>
    public bool AllowSecretKeyFromBrowser { get; init; }
}

[OptionsValidator]
internal sealed partial class ConsoleOptionsValidator : IValidateOptions<ConsoleOptions>;

[OptionsValidator]
internal sealed partial class InstanceOptionsValidator : IValidateOptions<InstanceOptions>;
