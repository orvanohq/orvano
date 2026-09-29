using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;

namespace Orvano.Platform.Application;

/// <summary>Install settings (AC-7): who may create a console account. Install admins only.</summary>
internal sealed class InstallService(PlatformStore store, ILogger<InstallService> logger) : IInstallSetupState, IInstallAdmins
{
    public const string Invite = "invite";
    public const string Open = "open";

    public Task<Outcome<InstallSettingsRow>> GetAsync(Guid userId, CancellationToken ct) =>
        store.ReadAsync<Outcome<InstallSettingsRow>>(async (db, ct) =>
            await db.IsInstallAdminAsync(userId, ct)
                ? await db.InstallSettings.AsNoTracking().SingleAsync(ct)
                : Failure.InstallAdminsOnly, ct);

    /// <summary>True while no install admin exists (spec 0006, AC-22). Needs no console user.</summary>
    public Task<bool> IsSetupRequiredAsync(CancellationToken ct) =>
        store.ReadAsync(async (db, ct) => !await db.InstallAdmins.AnyAsync(ct), ct);

    /// <summary>
    /// What the console needs before anyone signs in (spec 0006 AC-22, spec 0008 AC-13): whether the install still waits
    /// for its first admin, and whether sign up is open. Needs no console user.
    /// </summary>
    public Task<(bool SetupRequired, bool SignupOpen)> GetSetupAsync(CancellationToken ct) =>
        store.ReadAsync(async (db, ct) =>
            (!await db.InstallAdmins.AnyAsync(ct), await db.InstallSettings.AnyAsync(s => s.ConsoleSignup == Open, ct)), ct);

    /// <summary>True when the console user is an install admin (spec 0008, AC-12).</summary>
    public Task<bool> IsInstallAdminAsync(Guid userId, CancellationToken ct) =>
        store.ReadAsync((db, ct) => db.IsInstallAdminAsync(userId, ct), ct);

    public Task<Outcome<InstallSettingsRow>> UpdateAsync(Guid userId, string consoleSignup, CancellationToken ct) =>
        store.WriteAsync<InstallSettingsRow>(async (uow, ct) =>
        {
            if (consoleSignup is not (Invite or Open)) return Failure.Invalid("consoleSignup is invite or open.");
            if (!await uow.Db.IsInstallAdminAsync(userId, ct)) return Failure.InstallAdminsOnly;

            var settings = await uow.Db.InstallSettings.SingleAsync(ct);
            if (settings.ConsoleSignup != consoleSignup)
            {
                settings.ConsoleSignup = consoleSignup;
                settings.UpdatedAt = await uow.NowAsync(ct);
                await uow.Db.SaveChangesAsync(ct);
                await PlatformEvents.WriteAsync(uow.Tx, PlatformEvents.InstallSettingsUpdated, Actor.User(userId), "install",
                    new Dictionary<string, string>(), changed: ["consoleSignup"], ct: ct);
                logger.LogInformation("Install admin {UserId} set console sign up to {Mode}", userId, consoleSignup);
            }

            return settings;
        }, ct);
}
