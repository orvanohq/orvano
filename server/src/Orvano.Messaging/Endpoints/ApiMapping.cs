using Microsoft.AspNetCore.Http;
using Orvano.Core.Http;
using Orvano.Messaging.Application;
using Orvano.Messaging.Contracts;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;
using Api = Orvano.Contract;
using ErrorCode = Orvano.Contract.ErrorCode;

namespace Orvano.Messaging.Endpoints;

/// <summary>Maps use case results to HTTP: rows to the generated contract models, failures to problems.</summary>
internal static class ApiMapping
{
    public static IResult Problem(Failure failure) => ApiProblem.Result(failure.Status, failure.Code, failure.Detail);

    public static IResult Ok<T, TApi>(Outcome<T> outcome, Func<T, TApi> map) =>
        outcome.Succeeded ? TypedResults.Ok(map(outcome.Value!)) : Problem(outcome.Failure!);

    public static IResult NoContent(Outcome<Done> outcome) =>
        outcome.Succeeded ? TypedResults.NoContent() : Problem(outcome.Failure!);

    /// <summary>The request as the rules read it. A security value this server does not know stays unset and fails validation.</summary>
    public static SmtpSettingsDraft Draft(Api.SmtpSettingsInput input) => new(
        input.Host,
        input.Port,
        input.Security switch
        {
            Api.SmtpSecurity.Starttls => SmtpSecurity.StartTls,
            Api.SmtpSecurity.Tls => SmtpSecurity.Tls,
            Api.SmtpSecurity.None => SmtpSecurity.None,
            _ => null,
        },
        input.Username,
        input.Password,
        input.FromEmail,
        input.FromName,
        input.ReplyTo);

    /// <summary>Stored settings without the password, which never leaves the server.</summary>
    public static Api.SmtpSettings SmtpSettings(SmtpSettingsRow row) => new(
        row.Host,
        row.Port,
        SmtpSettingsRule.ParseSecurity(row.Security) switch
        {
            SmtpSecurity.StartTls => Api.SmtpSecurity.Starttls,
            SmtpSecurity.Tls => Api.SmtpSecurity.Tls,
            SmtpSecurity.None => Api.SmtpSecurity.None,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Security, null),
        },
        row.Username,
        row.PasswordCiphertext is not null,
        row.FromEmail,
        row.FromName,
        row.ReplyTo,
        row.UpdatedAt);

    public static Api.EmailPage EmailPage(Page<EmailRow> page) => new([.. page.Items.Select(EmailLogEntry)], page.NextCursor);

    /// <summary>A log row: the masked recipient only, never the content.</summary>
    public static Api.EmailLogEntry EmailLogEntry(EmailRow row) => new(
        row.Id.ToString(),
        row.Template switch
        {
            "verification" => Api.EmailTemplateName.Verification,
            "recovery" => Api.EmailTemplateName.Recovery,
            "magic_link" => Api.EmailTemplateName.MagicLink,
            "email_code" => Api.EmailTemplateName.EmailCode,
            EmailTemplateNames.ConsoleInvitation => Api.EmailTemplateName.ConsoleInvitation,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Template, null),
        },
        row.RecipientMasked,
        row.Status switch
        {
            EmailStatuses.Queued => Api.EmailStatus.Queued,
            EmailStatuses.Sent => Api.EmailStatus.Sent,
            EmailStatuses.Failed => Api.EmailStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Status, null),
        },
        row.SmtpSource switch
        {
            null => null,
            SmtpSources.Project => Api.SmtpSource.Project,
            SmtpSources.Install => Api.SmtpSource.Install,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.SmtpSource, null),
        },
        row.Attempts,
        row.ErrorCode switch
        {
            null => null,
            ErrorCode.SmtpUnreachable => Api.EmailFailureCode.SmtpUnreachable,
            ErrorCode.SmtpTlsFailed => Api.EmailFailureCode.SmtpTlsFailed,
            ErrorCode.SmtpAuthFailed => Api.EmailFailureCode.SmtpAuthFailed,
            ErrorCode.SmtpRejected => Api.EmailFailureCode.SmtpRejected,
            ErrorCode.SmtpTimeout => Api.EmailFailureCode.SmtpTimeout,
            ErrorCode.SmtpHostNotAllowed => Api.EmailFailureCode.SmtpHostNotAllowed,
            EmailFailures.NotConfigured => Api.EmailFailureCode.EmailNotConfigured,
            EmailFailures.ProjectNotActive => Api.EmailFailureCode.ProjectNotActive,
            EmailFailures.Expired => Api.EmailFailureCode.EmailExpired,
            EmailFailures.Unreadable => Api.EmailFailureCode.EmailUnreadable,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.ErrorCode, null),
        },
        row.CreatedAt,
        row.CompletedAt);

    public static Api.EmailTemplateCatalog EmailTemplateCatalog(IReadOnlyList<TemplateSummary> templates) =>
        new([.. templates.Select(t => new Api.EmailTemplateSummary(Kind(t.Info.Kind), t.Info.Name, t.Info.Description, t.IsCustom, t.UpdatedAt))]);

    /// <summary>A template with its variables, each with the sample value previews use.</summary>
    public static Api.EmailTemplate EmailTemplate(TemplateView view) => new(
        Kind(view.Info.Kind),
        Domain.EmailTemplateCatalog.Locale,
        view.Source.Subject,
        view.Source.Html,
        view.Source.Text,
        view.IsCustom,
        view.UpdatedAt,
        [.. view.Info.Variables.Select(v => new Api.TemplateVariable(v.Name, v.Description, view.Sample.Text(v.Name)))]);

    private static Api.AuthEmailKind Kind(AuthEmailKind kind) => kind switch
    {
        AuthEmailKind.Verification => Api.AuthEmailKind.Verification,
        AuthEmailKind.Recovery => Api.AuthEmailKind.Recovery,
        AuthEmailKind.MagicLink => Api.AuthEmailKind.MagicLink,
        AuthEmailKind.EmailCode => Api.AuthEmailKind.EmailCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>AC-4: the project's own settings only when it has them, and of the install only who it sends as.</summary>
    public static Api.ProjectSmtp ProjectSmtp(ProjectSmtpView view) => new(
        view.Source switch
        {
            SmtpSourceKind.Project => Api.SmtpSource.Project,
            SmtpSourceKind.Install => Api.SmtpSource.Install,
            SmtpSourceKind.None => Api.SmtpSource.None,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view.Source, null),
        },
        view.Settings is null ? null : SmtpSettings(view.Settings),
        view.Install is null ? null : new Api.EmailSender(view.Install.FromEmail, view.Install.FromName));
}
