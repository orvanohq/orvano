using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Core.Http;
using Orvano.Core.Secrets;
using Orvano.Messaging.Data;
using Orvano.Messaging.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Messaging.Application;

/// <summary>Whose SMTP settings a project sends through (AC-4).</summary>
internal enum SmtpSourceKind
{
    Project,
    Install,
    None,
}

/// <summary>What <c>consoleSmtp.get</c> shows: the source, the project's own row, and who the install sends as.</summary>
internal sealed record ProjectSmtpView(SmtpSourceKind Source, SmtpSettingsRow? Settings, SmtpSettingsRow? Install);

/// <summary>
/// Whose SMTP settings an operation is about, and who acts: a project's own row, or the install's (the row of
/// project <c>console</c>). The install's skips the private host rule (AC-3) and the project state check (AC-7).
/// </summary>
internal sealed record SmtpScope(string ProjectId, Guid UserId, bool Install)
{
    public static SmtpScope Of(ProjectCaller caller) => new(caller.ProjectId, caller.UserId, Install: false);

    public static SmtpScope OfInstall(Guid adminUserId) => new(SmtpSettingsService.InstallProjectId, adminUserId, Install: true);
}

/// <summary>
/// SMTP settings (spec 0009, AC-1 to AC-7): read, save, stop using, and send a test email, for a project or for
/// the install. The caller already passed <see cref="ProjectAccess"/> or the install admin check. The password is
/// sealed with <see cref="SecretBox"/> bound to its row, and no response, log line, or event ever carries it, the
/// host, or the username.
/// </summary>
internal sealed class SmtpSettingsService(
    MessagingStore store,
    SecretBox secrets,
    IProjectDirectory projects,
    IConsoleUserDirectory users,
    IHostResolver resolver,
    ISmtpSender sender,
    MessagingSettings settings,
    PublicUrl publicUrl,
    ILogger<SmtpSettingsService> logger)
{
    /// <summary>The reserved project whose row is the install's SMTP.</summary>
    public const string InstallProjectId = "console";

    /// <summary>The longest the lookup of a host name may take when settings are saved (AC-3).</summary>
    public static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The longest a test send waits in total (AC-6).</summary>
    public static readonly TimeSpan TestBudget = TimeSpan.FromSeconds(20);

    public Task<ProjectSmtpView> GetAsync(ProjectCaller caller, CancellationToken ct) =>
        store.ReadAsync(async (db, ct) =>
        {
            var rows = await db.SmtpSettings.AsNoTracking()
                .Where(s => s.ProjectId == caller.ProjectId || s.ProjectId == InstallProjectId)
                .ToListAsync(ct);
            var own = rows.SingleOrDefault(r => r.ProjectId == caller.ProjectId);
            var install = rows.SingleOrDefault(r => r.ProjectId == InstallProjectId);
            var source = own is not null ? SmtpSourceKind.Project : install is not null ? SmtpSourceKind.Install : SmtpSourceKind.None;
            return new ProjectSmtpView(source, own, install);
        }, ct);

    /// <summary>The install's own settings, or null when none are set (AC-7).</summary>
    public Task<SmtpSettingsRow?> GetInstallAsync(CancellationToken ct) => FindAsync(InstallProjectId, ct);

    public async Task<Outcome<SmtpSettingsRow>> UpdateAsync(SmtpScope caller, SmtpSettingsDraft draft, CancellationToken ct)
    {
        var stored = await FindAsync(caller.ProjectId, ct);
        if (!SmtpSettingsRule.TryValidate(draft, Identity(stored), out var valid, out var error)) return Failure.Invalid(error);
        if (!caller.Install)
        {
            if (!await HostAllowedAsync(valid.Host, ct)) return Failure.SmtpHostNotAllowed;
            if (await NotActiveAsync(caller.ProjectId, ct) is { } notActive) return notActive;
        }

        return await store.WriteAsync<SmtpSettingsRow>(async (uow, ct) =>
        {
            var row = await uow.Db.SmtpSettings.SingleOrDefaultAsync(s => s.ProjectId == caller.ProjectId, ct);
            // The row may have changed since the check above: the stored password is kept only for the same server.
            if (valid.PasswordSource == SmtpPasswordSource.Stored
                && !SmtpSettingsRule.MayReuseStoredPassword(Identity(row), valid.Host, valid.Port, valid.Username!))
            {
                return Failure.Invalid("password: Enter the password again when you change the host, port, or username.");
            }

            var security = SmtpSettingsRule.Wire(valid.Security);
            var now = await uow.NowAsync(ct);
            List<string> changed;
            if (row is null)
            {
                changed = ["host", "port", "security", "username", "password", "fromEmail", "fromName", "replyTo"];
                row = new SmtpSettingsRow { ProjectId = caller.ProjectId, Host = valid.Host, Security = security, FromEmail = valid.FromEmail, CreatedAt = now };
                uow.Db.SmtpSettings.Add(row);
            }
            else
            {
                changed = [];
                if (row.Host != valid.Host) changed.Add("host");
                if (row.Port != valid.Port) changed.Add("port");
                if (row.Security != security) changed.Add("security");
                if (row.Username != valid.Username) changed.Add("username");
                if (valid.PasswordSource == SmtpPasswordSource.Given || valid.PasswordSource == SmtpPasswordSource.None && row.PasswordCiphertext is not null) changed.Add("password");
                if (row.FromEmail != valid.FromEmail) changed.Add("fromEmail");
                if (row.FromName != valid.FromName) changed.Add("fromName");
                if (row.ReplyTo != valid.ReplyTo) changed.Add("replyTo");
            }

            row.Host = valid.Host;
            row.Port = valid.Port;
            row.Security = security;
            row.Username = valid.Username;
            row.PasswordCiphertext = valid.PasswordSource switch
            {
                SmtpPasswordSource.Given => secrets.Encrypt(Encoding.UTF8.GetBytes(valid.Password!), PasswordBinding(caller.ProjectId)),
                SmtpPasswordSource.Stored => row.PasswordCiphertext,
                _ => null,
            };
            row.FromEmail = valid.FromEmail;
            row.FromName = valid.FromName;
            row.ReplyTo = valid.ReplyTo;
            row.UpdatedByUserId = caller.UserId;
            row.UpdatedAt = now;
            await uow.Db.SaveChangesAsync(ct);

            await MessagingEvents.WriteSmtpAsync(uow.Tx, MessagingEvents.SmtpUpdated, caller.UserId, caller.ProjectId, changed, ct);
            logger.LogInformation("Console user {UserId} saved the SMTP settings of project {ProjectId}", caller.UserId, caller.ProjectId);
            return row;
        }, ct);
    }

    /// <summary>Succeeds also when nothing is stored (AC-5).</summary>
    public async Task<Outcome<Done>> DeleteAsync(SmtpScope caller, CancellationToken ct)
    {
        if (!caller.Install && await NotActiveAsync(caller.ProjectId, ct) is { } notActive) return notActive;

        return await store.WriteAsync<Done>(async (uow, ct) =>
        {
            if (await uow.Db.SmtpSettings.Where(s => s.ProjectId == caller.ProjectId).ExecuteDeleteAsync(ct) > 0)
            {
                await MessagingEvents.WriteSmtpAsync(uow.Tx, MessagingEvents.SmtpDeleted, caller.UserId, caller.ProjectId, null, ct);
                logger.LogInformation("Console user {UserId} deleted the SMTP settings of project {ProjectId}", caller.UserId, caller.ProjectId);
            }

            return new Done();
        }, ct);
    }

    /// <summary>
    /// Sends the fixed test email through <paramref name="draft"/>, saved or not, to the caller's own console email,
    /// and stores nothing (AC-6). One attempt, at most <see cref="TestBudget"/>. Returns the address it went to.
    /// </summary>
    public async Task<Outcome<string>> TestAsync(SmtpScope caller, SmtpSettingsDraft draft, CancellationToken ct)
    {
        var stored = await FindAsync(caller.ProjectId, ct);
        if (!SmtpSettingsRule.TryValidate(draft, Identity(stored), out var valid, out var error)) return Failure.Invalid(error);
        var publicOnly = !caller.Install && !settings.AllowPrivateHosts;
        // A name is checked when the connection is made, against the very addresses that are dialed.
        if (publicOnly && SmtpHost.TryGetLiteral(valid.Host, out var literal) && !GlobalUnicast.Contains(literal))
            return Failure.SmtpHostNotAllowed;

        var content = TestEmail.ForInstall();
        if (!caller.Install)
        {
            var project = await projects.GetAsync(caller.ProjectId, ct);
            if (project is null) return Failure.ProjectNotFound;
            if (project.Status != ProjectStatus.Active) return Failure.ProjectNotReady;
            content = TestEmail.ForProject(project.Name);
        }

        if ((await users.GetManyAsync([caller.UserId], ct)).SingleOrDefault() is not { } user)
            return Failure.Invalid("Your console account has no email address to send the test to.");

        var password = valid.PasswordSource switch
        {
            SmtpPasswordSource.Given => valid.Password,
            SmtpPasswordSource.Stored => Encoding.UTF8.GetString(secrets.Decrypt(stored!.PasswordCiphertext, PasswordBinding(caller.ProjectId))),
            _ => null,
        };
        var server = new SmtpServer(valid.Host, valid.Port, valid.Security, valid.Username, password, publicOnly);
        var email = new OutgoingEmail(
            valid.FromEmail, valid.FromName, valid.ReplyTo, user.Email,
            $"{Guid.CreateVersion7()}@{new Uri(publicUrl.Origin).Host}", content);

        if (await sender.SendAsync(server, email, TestBudget, ct) is { } failure)
        {
            logger.LogWarning(
                "A test email of project {ProjectId} failed with {ErrorCode} (SMTP reply {ReplyCode})",
                caller.ProjectId, SmtpOutcomes.Code(failure.Kind), failure.ReplyCode);
            return Failure.Smtp(failure);
        }

        logger.LogInformation("Console user {UserId} sent a test email through the settings of project {ProjectId}", caller.UserId, caller.ProjectId);
        return user.Email;
    }

    /// <summary>The associated data an SMTP password is sealed with: its table, row, and column (spec 0002).</summary>
    public static string PasswordBinding(string projectId) =>
        SecretBox.AssociatedData("messaging_smtp_settings", projectId, "password_ciphertext");

    private Task<SmtpSettingsRow?> FindAsync(string projectId, CancellationToken ct) =>
        store.ReadAsync((db, ct) => db.SmtpSettings.AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct), ct);

    private static StoredSmtpIdentity? Identity(SmtpSettingsRow? row) =>
        row is null ? null : new StoredSmtpIdentity(row.Host, row.Port, row.Username, row.PasswordCiphertext is not null);

    private async Task<Failure?> NotActiveAsync(string projectId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, ct);
        if (project is null) return Failure.ProjectNotFound;
        return project.Status == ProjectStatus.Active ? null : Failure.ProjectNotReady;
    }

    /// <summary>
    /// AC-3 when settings are saved: a literal must be global unicast, and so must every address a name resolves to
    /// right now. A name that doesn't resolve yet, or not within <see cref="LookupTimeout"/>, can be saved (DNS is
    /// often set up later); sending then fails until it does.
    /// </summary>
    private async Task<bool> HostAllowedAsync(string host, CancellationToken ct)
    {
        if (settings.AllowPrivateHosts) return true;
        if (SmtpHost.TryGetLiteral(host, out var literal)) return GlobalUnicast.Contains(literal);

        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookup.CancelAfter(LookupTimeout);
        try
        {
            return GlobalUnicast.ContainsAll(await resolver.ResolveAsync(host, lookup.Token));
        }
        catch (SocketException)
        {
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return true;
        }
    }
}
