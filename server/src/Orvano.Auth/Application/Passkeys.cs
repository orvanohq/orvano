using System.Security.Cryptography;
using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;
using Npgsql;
using NpgsqlTypes;
using Orvano.Auth.Domain;
using Orvano.Platform.Contracts;

namespace Orvano.Auth.Application;

/// <summary>A passkey row as a ceremony reads it.</summary>
internal sealed record StoredPasskey(Guid Id, Guid UserId, byte[] PublicKey, long SignCount, string RpId);

/// <summary>A passkey as the API lists it (the contract's <c>Passkey</c>).</summary>
internal sealed record PasskeyView(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool Synced, bool Active);

/// <summary>A credential named in options: its ID (base64url) and transports.</summary>
internal sealed record CredentialRef(string Id, IReadOnlyList<string> Transports);

/// <summary>
/// The parts of a registration's options that vary (AC-20); the fixed ones (algorithms, timeout, authenticator rules,
/// attestation) come from <see cref="PasskeyRules"/>.
/// </summary>
internal sealed record CreationOptionsView(
    string RpId, string RpName, string UserHandle, string UserName, string DisplayName, string Challenge, IReadOnlyList<CredentialRef> ExcludeCredentials);

/// <summary>A started registration (the contract's <c>PasskeyRegistration</c>).</summary>
internal sealed record RegistrationView(Guid ChallengeId, CreationOptionsView Options);

/// <summary>The parts of a sign in, step two, or step up's options that vary (AC-11, AC-19, AC-23).</summary>
internal sealed record RequestOptionsView(string Challenge, string RpId, IReadOnlyList<CredentialRef> AllowCredentials);

/// <summary>A challenge for a passkey to sign (the contract's <c>PasskeyChallenge</c>).</summary>
internal sealed record PasskeyChallengeView(Guid ChallengeId, RequestOptionsView Options);

/// <summary>A new passkey as the browser or platform sent it, binary values still base64url.</summary>
internal sealed record AttestationInput(string Id, string RawId, string Type, string ClientDataJson, string AttestationObject, IReadOnlyList<string>? Transports);

/// <summary>A passkey's answer as the browser or platform sent it, binary values still base64url.</summary>
internal sealed record AssertionInput(string Id, string RawId, string Type, string ClientDataJson, string AuthenticatorData, string Signature, string? UserHandle);

/// <summary>A passkey's answer to a ticket's or a step up's challenge: the challenge ID as sent, and the credential.</summary>
internal sealed record PasskeyAnswerInput(string? ChallengeId, AssertionInput? Credential);

/// <summary>A challenge row taken out by its one conditional delete: what the ceremony checks against.</summary>
internal sealed record ConsumedChallenge(Guid Id, byte[] Hash, Guid? UserId, Guid? TicketId);

/// <summary>A registration Fido2NetLib accepted, ready to store.</summary>
internal sealed record NewPasskey(byte[] CredentialId, byte[] PublicKey, uint SignCount, Guid? AaGuid, bool BackupEligible, bool BackedUp, string[] Transports);

/// <summary>
/// The outcome of an assertion (AC-22): the passkey it named and its new counter and backup state when every check
/// passed; <see cref="Regressed"/> when only the counter rule failed, so the caller writes
/// <c>auth.passkey.counter_regressed</c>.
/// </summary>
internal sealed record AssertionCheck(StoredPasskey? Passkey, uint SignCount, bool BackedUp, bool Regressed)
{
    public static AssertionCheck Failed { get; } = new(null, 0, false, false);

    public bool Valid => Passkey is not null && !Regressed;
}

/// <summary>
/// The <c>auth_passkeys</c> and <c>auth_webauthn_challenges</c> rows (spec 0013). Challenges are stored only as
/// SHA-256 and taken out with one conditional delete, so each works once. Every write runs in the caller's
/// transaction; the caller holds the user lock first (the module's lock order) whenever it touches a user's rows.
/// </summary>
internal static class PasskeyRows
{
    /// <summary>A new challenge row, expiring in 5 minutes; answers its ID and the 32 challenge bytes.</summary>
    public static async Task<(Guid Id, byte[] Challenge)> CreateChallengeAsync(
        AuthUnitOfWork uow, string projectId, string purpose, Guid? userId, Guid? ticketId, CancellationToken ct)
    {
        var challenge = WebAuthnChallenge.New();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO orvano.auth_webauthn_challenges (project_id, purpose, challenge_hash, user_id, ticket_id, expires_at)
            VALUES (@project, @purpose, @hash, @user, @ticket, now() + @lifetime)
            RETURNING id
            """, uow.Tx.Connection, uow.Tx);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("purpose", purpose);
        insert.Parameters.AddWithValue("hash", WebAuthnChallenge.Hash(challenge));
        insert.Parameters.AddWithValue("user", NpgsqlDbType.Uuid, (object?)userId ?? DBNull.Value);
        insert.Parameters.AddWithValue("ticket", NpgsqlDbType.Uuid, (object?)ticketId ?? DBNull.Value);
        insert.Parameters.AddWithValue("lifetime", AuthTimings.WebAuthnChallenge);
        return ((Guid)(await insert.ExecuteScalarAsync(ct))!, challenge);
    }

    /// <summary>
    /// Takes a live challenge out with one conditional delete (AC-21, AC-22): it must have this project and purpose,
    /// and the given user or ticket when one is given. Null when there is none, so it can't be used twice.
    /// </summary>
    public static async Task<ConsumedChallenge?> ConsumeChallengeAsync(
        AuthUnitOfWork uow, string projectId, Guid challengeId, string purpose, Guid? userId, Guid? ticketId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand(
            """
            DELETE FROM orvano.auth_webauthn_challenges
            WHERE id = @id AND project_id = @project AND purpose = @purpose AND expires_at > now()
              AND (@user::uuid IS NULL OR user_id = @user) AND (@ticket::uuid IS NULL OR ticket_id = @ticket)
            RETURNING challenge_hash, user_id, ticket_id
            """, uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("id", challengeId);
        delete.Parameters.AddWithValue("project", projectId);
        delete.Parameters.AddWithValue("purpose", purpose);
        delete.Parameters.AddWithValue("user", NpgsqlDbType.Uuid, (object?)userId ?? DBNull.Value);
        delete.Parameters.AddWithValue("ticket", NpgsqlDbType.Uuid, (object?)ticketId ?? DBNull.Value);
        await using var reader = await delete.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ConsumedChallenge(
            challengeId,
            reader.GetFieldValue<byte[]>(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2));
    }

    /// <summary>The project's passkey with this credential ID, active or not; null when there is none.</summary>
    public static async Task<StoredPasskey?> FindAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string projectId, byte[] credentialId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, user_id, public_key, sign_count, rp_id FROM orvano.auth_passkeys
            WHERE project_id = @project AND credential_id = @credential
            """, conn, tx);
        cmd.Parameters.AddWithValue("project", projectId);
        cmd.Parameters.AddWithValue("credential", credentialId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new StoredPasskey(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<byte[]>(2), reader.GetInt64(3), reader.GetString(4));
    }

    /// <summary>The user's passkeys under <paramref name="rpId"/>, oldest first, as options name them.</summary>
    public static async Task<IReadOnlyList<CredentialRef>> RefsAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid userId, string? rpId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT credential_id, transports FROM orvano.auth_passkeys
            WHERE user_id = @user AND rp_id = @rpId
            ORDER BY created_at, id
            """, conn, tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("rpId", NpgsqlDbType.Text, (object?)rpId ?? DBNull.Value);
        var refs = new List<CredentialRef>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) refs.Add(new CredentialRef(Base64Codec.Encode(reader.GetFieldValue<byte[]>(0)), reader.GetFieldValue<string[]>(1)));
        return refs;
    }

    /// <summary>How many passkey rows the user has, active or not (the limit of 10 counts them all).</summary>
    public static async Task<int> CountAsync(AuthUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*)::int FROM orvano.auth_passkeys WHERE user_id = @user", uow.Tx.Connection, uow.Tx);
        cmd.Parameters.AddWithValue("user", userId);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Stores a new passkey; null when its credential ID is already in the project (AC-21).</summary>
    public static async Task<PasskeyView?> InsertAsync(
        AuthUnitOfWork uow, string projectId, Guid userId, NewPasskey passkey, string name, string rpId, CancellationToken ct)
    {
        await using var insert = new NpgsqlCommand(
            $"""
            INSERT INTO orvano.auth_passkeys (
                project_id, user_id, credential_id, public_key, sign_count, aaguid, name, transports, backup_eligible, backed_up, rp_id)
            VALUES (@project, @user, @credential, @key, @count, @aaguid, @name, @transports, @eligible, @backedUp, @rpId)
            ON CONFLICT (project_id, credential_id) DO NOTHING
            RETURNING {ViewColumns}
            """, uow.Tx.Connection, uow.Tx);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("user", userId);
        insert.Parameters.AddWithValue("credential", passkey.CredentialId);
        insert.Parameters.AddWithValue("key", passkey.PublicKey);
        insert.Parameters.AddWithValue("count", (long)passkey.SignCount);
        insert.Parameters.AddWithValue("aaguid", NpgsqlDbType.Uuid, (object?)passkey.AaGuid ?? DBNull.Value);
        insert.Parameters.AddWithValue("name", name);
        insert.Parameters.AddWithValue("transports", NpgsqlDbType.Array | NpgsqlDbType.Text, passkey.Transports);
        insert.Parameters.AddWithValue("eligible", passkey.BackupEligible);
        insert.Parameters.AddWithValue("backedUp", passkey.BackedUp);
        insert.Parameters.AddWithValue("rpId", rpId);
        insert.Parameters.AddWithValue("current", rpId);
        await using var reader = await insert.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? View(reader) : null;
    }

    /// <summary>
    /// Records a passed assertion (AC-22): the new counter, backup state, and last use, only while the passkey is still
    /// active under <paramref name="rpId"/> and the counter still moves forward (or both stay 0). False otherwise, so a
    /// parallel assertion that raced past the same counter fails.
    /// </summary>
    public static async Task<bool> UseAsync(AuthUnitOfWork uow, Guid passkeyId, string rpId, uint signCount, bool backedUp, CancellationToken ct)
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE orvano.auth_passkeys SET sign_count = @count, backed_up = @backedUp, last_used_at = now()
            WHERE id = @id AND rp_id = @rpId AND (sign_count < @count OR (sign_count = 0 AND @count = 0))
            """, uow.Tx.Connection, uow.Tx);
        update.Parameters.AddWithValue("count", (long)signCount);
        update.Parameters.AddWithValue("backedUp", backedUp);
        update.Parameters.AddWithValue("id", passkeyId);
        update.Parameters.AddWithValue("rpId", rpId);
        return await update.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>The user's passkeys, oldest first; <c>active</c> while made for <paramref name="currentRpId"/> (AC-2).</summary>
    public static async Task<IReadOnlyList<PasskeyView>> ListAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid userId, string? currentRpId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT {ViewColumns} FROM orvano.auth_passkeys WHERE user_id = @user ORDER BY created_at, id", conn, tx);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("current", NpgsqlDbType.Text, (object?)currentRpId ?? DBNull.Value);
        var views = new List<PasskeyView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) views.Add(View(reader));
        return views;
    }

    /// <summary>Renames one of the user's passkeys; null when the user has no such passkey.</summary>
    public static async Task<PasskeyView?> RenameAsync(AuthUnitOfWork uow, Guid userId, Guid passkeyId, string name, string? currentRpId, CancellationToken ct)
    {
        await using var update = new NpgsqlCommand(
            $"UPDATE orvano.auth_passkeys SET name = @name WHERE id = @id AND user_id = @user RETURNING {ViewColumns}", uow.Tx.Connection, uow.Tx);
        update.Parameters.AddWithValue("name", name);
        update.Parameters.AddWithValue("id", passkeyId);
        update.Parameters.AddWithValue("user", userId);
        update.Parameters.AddWithValue("current", NpgsqlDbType.Text, (object?)currentRpId ?? DBNull.Value);
        await using var reader = await update.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? View(reader) : null;
    }

    /// <summary>Deletes one of the user's passkeys; false when the user has no such passkey.</summary>
    public static async Task<bool> DeleteAsync(AuthUnitOfWork uow, Guid userId, Guid passkeyId, CancellationToken ct)
    {
        await using var delete = new NpgsqlCommand("DELETE FROM orvano.auth_passkeys WHERE id = @id AND user_id = @user", uow.Tx.Connection, uow.Tx);
        delete.Parameters.AddWithValue("id", passkeyId);
        delete.Parameters.AddWithValue("user", userId);
        return await delete.ExecuteNonQueryAsync(ct) == 1;
    }

    private const string ViewColumns = "id, name, created_at, last_used_at, backed_up, rp_id IS NOT DISTINCT FROM @current";

    private static PasskeyView View(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetFieldValue<DateTimeOffset>(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5));
}

/// <summary>
/// The two WebAuthn ceremonies (spec 0013, AC-21, AC-22), on Fido2NetLib. The domain reads <c>clientDataJSON</c>
/// first: its challenge must hash to the consumed row's, and its origin must pass AC-4 (AC-3 for <c>console</c>).
/// Only that one matched origin is handed to Fido2NetLib, which then checks the type, the challenge, the origin, the
/// RP ID hash, the user present and user verified flags, the algorithm, and the signature.
/// </summary>
internal sealed class WebAuthnVerifier(IWebOriginPolicy origins)
{
    private static readonly IReadOnlyList<PubKeyCredParam> Algorithms =
    [
        new(COSE.Algorithm.ES256, PublicKeyCredentialType.PublicKey),
        new(COSE.Algorithm.EdDSA, PublicKeyCredentialType.PublicKey),
        new(COSE.Algorithm.RS256, PublicKeyCredentialType.PublicKey),
    ];

    /// <summary>
    /// The origin a ceremony may name (AC-3, AC-4): for <c>console</c>, only <c>ORVANO_PUBLIC_URL</c>'s; for an app
    /// project, <c>https://</c> + RP ID, a listed Android certificate's origin, or a web platform on the RP ID or a
    /// subdomain. Null when refused.
    /// </summary>
    public async Task<string?> MatchOriginAsync(MethodPolicy policy, string projectId, string origin, CancellationToken ct)
    {
        if (policy.RpId is not { } rpId) return null;
        if (policy.ConsoleOrigin is { } console) return string.Equals(origin, console, StringComparison.Ordinal) ? origin : null;
        return PasskeyOrigins.Classify(origin, rpId, policy.AndroidFingerprints) switch
        {
            OriginMatch.Native => origin,
            OriginMatch.Web => await origins.AllowsAsync(projectId, origin, ct) ? origin : null,
            _ => null,
        };
    }

    /// <summary>
    /// Verifies a registration (AC-21) against the consumed challenge; null on any failure. Any attestation format
    /// Fido2NetLib can parse is accepted, with no trust path.
    /// </summary>
    public async Task<NewPasskey?> VerifyRegistrationAsync(
        MethodPolicy policy, string projectId, ConsumedChallenge challenge, Guid userId, AttestationInput input, CancellationToken ct)
    {
        if (await CheckClientDataAsync(policy, projectId, challenge, input.ClientDataJson, ClientData.TypeCreate, ct) is not var (clientData, origin)) return null;
        if (input.Type != PasskeyRules.CredentialType
            || !Base64Codec.TryDecode(input.RawId, out var rawId)
            || !Base64Codec.TryDecode(input.ClientDataJson, out var clientDataJson)
            || !Base64Codec.TryDecode(input.AttestationObject, out var attestation))
        {
            return null;
        }

        var handle = PasskeyRules.UserHandle(userId);
        var options = new CredentialCreateOptions
        {
            Rp = new PublicKeyCredentialRpEntity(policy.RpId!, policy.RpName ?? policy.RpId!, null),
            User = new Fido2User { Id = handle, Name = userId.ToString(), DisplayName = userId.ToString() },
            Challenge = clientData.Challenge,
            PubKeyCredParams = Algorithms,
            Timeout = PasskeyRules.TimeoutMs,
            Attestation = AttestationConveyancePreference.None,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            ExcludeCredentials = [],
        };
        var response = new AuthenticatorAttestationRawResponse
        {
            Id = input.Id,
            RawId = rawId,
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAttestationRawResponse.AttestationResponse
            {
                AttestationObject = attestation,
                ClientDataJson = clientDataJson,
                Transports = [],
            },
            ClientExtensionResults = new AuthenticationExtensionsClientOutputs(),
        };

        try
        {
            var made = await Library(policy, origin).MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true),
            }, ct);
            var transports = (input.Transports ?? []).Where(PasskeyRules.Transports.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return new NewPasskey(made.Id, made.PublicKey, made.SignCount, made.AaGuid == Guid.Empty ? null : made.AaGuid, made.IsBackupEligible, made.IsBackedUp, transports);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Fido2NetLib refuses with Fido2VerificationException, and malformed CBOR or keys throw their own types;
            // every one is the same 401 invalid_passkey, and its message (which may quote the input) is never logged.
            return null;
        }
    }

    /// <summary>
    /// Verifies an assertion (AC-22) against the consumed challenge and the project's passkey with the credential ID it
    /// names, which must be active. A user handle in the answer must be that passkey's user.
    /// </summary>
    public async Task<AssertionCheck> VerifyAssertionAsync(
        MethodPolicy policy, string projectId, ConsumedChallenge challenge, AssertionInput input, NpgsqlConnection conn, CancellationToken ct)
    {
        if (await CheckClientDataAsync(policy, projectId, challenge, input.ClientDataJson, ClientData.TypeGet, ct) is not var (clientData, origin))
            return AssertionCheck.Failed;
        if (input.Type != PasskeyRules.CredentialType
            || !Base64Codec.TryDecode(input.RawId, out var rawId)
            || !Base64Codec.TryDecode(input.ClientDataJson, out var clientDataJson)
            || !Base64Codec.TryDecode(input.AuthenticatorData, out var authenticatorData)
            || !Base64Codec.TryDecode(input.Signature, out var signature))
        {
            return AssertionCheck.Failed;
        }

        byte[]? userHandle = null;
        if (input.UserHandle is { } handleText && (!Base64Codec.TryDecode(handleText, out userHandle) || userHandle.Length == 0)) return AssertionCheck.Failed;

        if (await PasskeyRows.FindAsync(conn, null, projectId, rawId, ct) is not { } passkey || passkey.RpId != policy.RpId) return AssertionCheck.Failed;

        var options = new AssertionOptions
        {
            Challenge = clientData.Challenge,
            RpId = policy.RpId,
            Timeout = PasskeyRules.TimeoutMs,
            UserVerification = UserVerificationRequirement.Required,
            AllowCredentials = [],
        };
        var response = new AuthenticatorAssertionRawResponse
        {
            Id = input.Id,
            RawId = rawId,
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAssertionRawResponse.AssertionResponse
            {
                AuthenticatorData = authenticatorData,
                Signature = signature,
                ClientDataJson = clientDataJson,
                UserHandle = userHandle,
            },
            ClientExtensionResults = new AuthenticationExtensionsClientOutputs(),
        };
        var owner = PasskeyRules.UserHandle(passkey.UserId);

        try
        {
            var verified = await Library(policy, origin).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = (uint)passkey.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) => Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(owner)),
            }, ct);
            // AC-22 is applied here too, so the rule (and its counter_regressed event) never depends on how a Fido2NetLib
            // version reads it, such as a passkey that stored 5 and now answers 0.
            return PasskeyRules.CounterRegressed(passkey.SignCount, verified.SignCount)
                ? new AssertionCheck(passkey, 0, false, true)
                : new AssertionCheck(passkey, verified.SignCount, verified.IsBackedUp, false);
        }
        catch (Fido2VerificationException e) when (e.Code == Fido2ErrorCode.InvalidSignCount)
        {
            return new AssertionCheck(passkey, 0, false, true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return AssertionCheck.Failed;
        }
    }

    private async Task<(ClientData ClientData, string Origin)?> CheckClientDataAsync(
        MethodPolicy policy, string projectId, ConsumedChallenge challenge, string clientDataJson, string type, CancellationToken ct)
    {
        if (ClientData.TryParse(clientDataJson) is not { } clientData || clientData.Type != type) return null;
        if (!CryptographicOperations.FixedTimeEquals(WebAuthnChallenge.Hash(clientData.Challenge), challenge.Hash)) return null;
        return await MatchOriginAsync(policy, projectId, clientData.Origin, ct) is { } origin ? (clientData, origin) : null;
    }

    private static Fido2 Library(MethodPolicy policy, string origin) => new(new Fido2Configuration
    {
        RPID = policy.RpId!,
        RPName = policy.RpName ?? policy.RpId!,
        Origins = new HashSet<string>(StringComparer.Ordinal) { origin },
    });
}
