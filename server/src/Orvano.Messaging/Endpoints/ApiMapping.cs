using Microsoft.AspNetCore.Http;
using Orvano.Core.Http;
using Orvano.Messaging.Application;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;
using Api = Orvano.Contract;

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
