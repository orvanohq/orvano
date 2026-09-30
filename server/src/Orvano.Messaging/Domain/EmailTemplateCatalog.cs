using System.Globalization;
using Orvano.Messaging.Contracts;

namespace Orvano.Messaging.Domain;

/// <summary>A value a template can use, as the editor lists it.</summary>
/// <param name="Name">The path written between <c>{{</c> and <c>}}</c>, for example <c>user.email</c>.</param>
/// <param name="Description">What it holds.</param>
internal sealed record TemplateVariable(string Name, string Description);

/// <summary>One of the four auth email templates.</summary>
/// <param name="Kind">Which one.</param>
/// <param name="Wire">Its stored and wire name, for example <c>magic_link</c>.</param>
/// <param name="Name">Its name in the console.</param>
/// <param name="Description">When it is sent.</param>
/// <param name="Variables">The values it can use, and no others.</param>
internal sealed record EmailTemplateInfo(
    AuthEmailKind Kind, string Wire, string Name, string Description, IReadOnlyList<TemplateVariable> Variables)
{
    /// <summary>True when <paramref name="path"/> is one of <see cref="Variables"/>.</summary>
    public bool Allows(string path) => Variables.Any(v => v.Name == path);
}

/// <summary>The values one render fills a template with. Never log them.</summary>
/// <param name="ProjectName"><c>project.name</c>.</param>
/// <param name="UserEmail"><c>user.email</c>.</param>
/// <param name="UserName"><c>user.name</c>, empty when the user has none.</param>
/// <param name="ActionUrl"><c>action_url</c>, for the three link templates.</param>
/// <param name="Code"><c>code</c>, for the email code template.</param>
/// <param name="ExpiresInMinutes"><c>expires_in_minutes</c>.</param>
internal sealed record TemplateValues(
    string ProjectName, string UserEmail, string UserName, string? ActionUrl, string? Code, int ExpiresInMinutes)
{
    /// <summary>The value of the variable called <paramref name="name"/>, as text.</summary>
    public string Text(string name) => name switch
    {
        EmailTemplateCatalog.ProjectName => ProjectName,
        EmailTemplateCatalog.UserEmail => UserEmail,
        EmailTemplateCatalog.UserName => UserName,
        EmailTemplateCatalog.ActionUrl => ActionUrl ?? "",
        EmailTemplateCatalog.Code => Code ?? "",
        EmailTemplateCatalog.ExpiresInMinutes => ExpiresInMinutes.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}

/// <summary>
/// The four auth email templates and their variables (spec 0009, template variables). Variables are snake_case, as
/// Liquid conventionally is.
/// </summary>
internal static class EmailTemplateCatalog
{
    public const string ProjectName = "project.name";
    public const string UserEmail = "user.email";
    public const string UserName = "user.name";
    public const string ActionUrl = "action_url";
    public const string Code = "code";
    public const string ExpiresInMinutes = "expires_in_minutes";

    /// <summary>The only locale for now; the column is already part of the key.</summary>
    public const string Locale = "en";

    public const string SampleActionUrl = "https://example.com/auth/confirm?token=sample";
    public const string SampleCode = "428613";
    public const int SampleLinkMinutes = 60;
    public const int SampleCodeMinutes = 10;

    private static readonly TemplateVariable Project = new(ProjectName, "Your project's name.");
    private static readonly TemplateVariable Email = new(UserEmail, "The address this email is sent to.");
    private static readonly TemplateVariable Name = new(UserName, "The user's name. Empty when they haven't set one.");
    private static readonly TemplateVariable Url = new(ActionUrl, "The link the user opens to finish.");
    private static readonly TemplateVariable TheCode = new(Code, "The code the user types in.");
    private static readonly TemplateVariable Expires = new(ExpiresInMinutes, "How many minutes the link or code works.");

    /// <summary>The four templates, in the order the console lists them.</summary>
    public static IReadOnlyList<EmailTemplateInfo> All { get; } =
    [
        new(AuthEmailKind.Verification, "verification", "Email verification", "Sent to confirm a user owns their email address.", [Project, Email, Name, Url, Expires]),
        new(AuthEmailKind.Recovery, "recovery", "Password reset", "Sent when a user asks to reset their password.", [Project, Email, Name, Url, Expires]),
        new(AuthEmailKind.MagicLink, "magic_link", "Magic link", "Sent when a user signs in with a link instead of a password.", [Project, Email, Name, Url, Expires]),
        new(AuthEmailKind.EmailCode, "email_code", "Email code", "Sent when a user signs in with a one time code.", [Project, Email, Name, TheCode, Expires]),
    ];

    public static EmailTemplateInfo Get(AuthEmailKind kind) => All.Single(t => t.Kind == kind);

    /// <summary>The template whose wire name is <paramref name="wire"/>, or null when there is none.</summary>
    public static EmailTemplateInfo? Find(string wire) => All.SingleOrDefault(t => t.Wire == wire);

    /// <summary>
    /// The values previews and test emails use: the catalog's samples, with the real project name and the caller's
    /// own email and name.
    /// </summary>
    public static TemplateValues Sample(AuthEmailKind kind, string projectName, string callerEmail, string? callerName) =>
        kind == AuthEmailKind.EmailCode
            ? new(projectName, callerEmail, callerName ?? "", null, SampleCode, SampleCodeMinutes)
            : new(projectName, callerEmail, callerName ?? "", SampleActionUrl, null, SampleLinkMinutes);
}
