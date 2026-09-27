using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Data;

namespace Orvano.Platform.Application;

/// <summary>Install settings (AC-7): who may create a console account. Install admins only.</summary>
internal sealed class InstallService(PlatformStore store, ILogger<InstallService> logger) : IInstallSetupState
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
