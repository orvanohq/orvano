using Orvano.Contract;

namespace Orvano.Messaging.Domain;

/// <summary>What went wrong with one SMTP attempt (spec 0009, SMTP outcomes).</summary>
internal enum SmtpFailureKind
{
    /// <summary>The name did not resolve, or the connection was refused or reset.</summary>
    Unreachable,

    /// <summary>The attempt's time budget ran out.</summary>
    Timeout,

    /// <summary>The TLS handshake failed, the certificate is invalid, or STARTTLS is not offered.</summary>
    TlsFailed,

    /// <summary>The server refused the username or password.</summary>
    AuthFailed,

    /// <summary>The server answered MAIL, RCPT, or DATA with an error.</summary>
    Rejected,

    /// <summary>The host is, or resolves to, an address outside global unicast (AC-3).</summary>
    HostNotAllowed,
}

/// <summary>One failed SMTP attempt.</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="ReplyCode">The server's SMTP reply code, when it sent one.</param>
/// <param name="ReplyText">The server's reply text, when it sent one. Shown only for <see cref="SmtpFailureKind.Rejected"/>.</param>
internal sealed record SmtpFailure(SmtpFailureKind Kind, int? ReplyCode = null, string? ReplyText = null);

/// <summary>
/// The one mapping from an SMTP failure to its error code, to what the worker does next, and to what a test
/// answers (spec 0009, SMTP outcomes). No other exception text ever reaches a client.
/// </summary>
internal static class SmtpOutcomes
{
    /// <summary>The longest <c>detail</c> of a rejected email: the reply code and text, cut here.</summary>
    public const int MaxRejectedDetail = 200;

    /// <summary>The stable error code, one of the contract's <c>EmailFailureCode</c> values.</summary>
    public static string Code(SmtpFailureKind kind) => kind switch
    {
        SmtpFailureKind.Unreachable => ErrorCode.SmtpUnreachable,
        SmtpFailureKind.Timeout => ErrorCode.SmtpTimeout,
        SmtpFailureKind.TlsFailed => ErrorCode.SmtpTlsFailed,
        SmtpFailureKind.AuthFailed => ErrorCode.SmtpAuthFailed,
        SmtpFailureKind.Rejected => ErrorCode.SmtpRejected,
        SmtpFailureKind.HostNotAllowed => ErrorCode.SmtpHostNotAllowed,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Whether the worker tries again: a server that can't be reached or is slow, and any 4xx reply. A TLS failure,
    /// a 5xx reply, and a host that is not allowed fail at once.
    /// </summary>
    public static bool Retries(SmtpFailure failure) => failure.Kind switch
    {
        SmtpFailureKind.Unreachable or SmtpFailureKind.Timeout => true,
        SmtpFailureKind.AuthFailed or SmtpFailureKind.Rejected => failure.ReplyCode is >= 400 and < 500,
        SmtpFailureKind.TlsFailed or SmtpFailureKind.HostNotAllowed => false,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null),
    };

    /// <summary>The HTTP status a test send answers. A test never retries, so a 4xx reply is a failure too.</summary>
    public static int TestStatus(SmtpFailureKind kind) => kind switch
    {
        SmtpFailureKind.Timeout => 504,
        SmtpFailureKind.HostNotAllowed => 400,
        SmtpFailureKind.Unreachable or SmtpFailureKind.TlsFailed or SmtpFailureKind.AuthFailed or SmtpFailureKind.Rejected => 502,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The problem <c>detail</c>, safe to show: a fixed sentence, or for a rejected email the server's reply code
    /// and text with control characters removed, cut to <see cref="MaxRejectedDetail"/> characters.
    /// </summary>
    public static string Detail(SmtpFailure failure) => failure.Kind switch
    {
        SmtpFailureKind.Unreachable => "Couldn't connect to the SMTP server.",
        SmtpFailureKind.Timeout => "The SMTP server didn't answer in time.",
        SmtpFailureKind.TlsFailed => "The secure connection failed. Check the Security setting and the server's certificate.",
        SmtpFailureKind.AuthFailed => "The SMTP server refused the username or password.",
        SmtpFailureKind.HostNotAllowed => HostNotAllowedDetail,
        SmtpFailureKind.Rejected => RejectedDetail(failure),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, null),
    };

    public const string HostNotAllowedDetail = "The SMTP host points to a private network address, which projects can't use.";

    private static string RejectedDetail(SmtpFailure failure)
    {
        if (failure.ReplyCode is not { } code) return "The SMTP server refused this email.";
        var text = new string([.. (failure.ReplyText ?? "").Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        var detail = text.Length == 0 ? $"{code}" : $"{code} {text}";
        return detail.Length <= MaxRejectedDetail ? detail : detail[..MaxRejectedDetail];
    }
}
