using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Orvano.Messaging.Application;

/// <summary>The <c>Orvano.Messaging</c> meter (spec 0009). Tags carry names and codes only, never an address or a host.</summary>
internal static class MessagingTelemetry
{
    /// <summary>The meter name to subscribe to in OpenTelemetry.</summary>
    public const string MeterName = "Orvano.Messaging";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Sent = Meter.CreateCounter<long>(
        "orvano.messaging.emails.sent", unit: "{email}", description: "Emails an SMTP server accepted.");

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "orvano.messaging.emails.failed", unit: "{email}", description: "Emails that ended failed.");

    private static readonly Histogram<double> SendDuration = Meter.CreateHistogram<double>(
        "orvano.messaging.email.send.duration", unit: "s", description: "How long one SMTP send attempt took.");

    public static void RecordSent(string template, string smtpSource) =>
        Sent.Add(1, new TagList { { "template", template }, { "smtp.source", smtpSource } });

    public static void RecordFailed(string template, string? smtpSource, string errorCode) =>
        Failed.Add(1, new TagList { { "template", template }, { "smtp.source", smtpSource ?? "none" }, { "error.code", errorCode } });

    public static void RecordAttempt(TimeSpan elapsed, string template, string smtpSource, string? errorCode) =>
        SendDuration.Record(elapsed.TotalSeconds, new TagList { { "template", template }, { "smtp.source", smtpSource }, { "error.code", errorCode ?? "none" } });
}
