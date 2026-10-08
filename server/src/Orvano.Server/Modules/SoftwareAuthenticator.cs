using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orvano.Contract;

namespace Orvano.Server.Modules;

/// <summary>
/// The <c>Test</c> only software authenticator (spec 0013, AC-45): it makes passkeys with new P-256 keys, kept in
/// process memory for the run, and signs challenges with them, so tests and scenarios run real WebAuthn ceremonies
/// with no browser. Registrations use attestation <c>none</c>. Knobs let a test send a wrong RP ID, drop the user
/// verified flag, set the backup state, choose the counter, or leave out the user handle.
/// </summary>
internal sealed class SoftwareAuthenticator
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte BackupEligible = 0x08;
    private const byte BackedUp = 0x10;
    private const byte AttestedData = 0x40;

    private sealed record Key(ECDsa Signer, byte[] UserHandle, bool BackupEligible);

    private readonly ConcurrentDictionary<string, Key> keys = new(StringComparer.Ordinal);

    /// <summary>Answers <c>navigator.credentials.create</c> for the options, as a platform authenticator would.</summary>
    public PasskeyRegistrationCredential Create(TestCreatePasskeyCredentialRequest request)
    {
        var options = request.Options;
        var backedUp = request.BackedUp ?? false;
        var credentialId = RandomNumberGenerator.GetBytes(16);
        var id = Base64Url.EncodeToString(credentialId);
        var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        keys[id] = new Key(signer, Base64Url.DecodeFromChars(options.User.Id), backedUp);

        var flags = (byte)(UserPresent | AttestedData | (request.UserVerified ?? true ? UserVerified : 0) | (backedUp ? BackupEligible | BackedUp : 0));
        var authData = new List<byte>(AuthDataHead(request.RpId ?? options.Rp.Id, flags, 0));
        authData.AddRange(new byte[16]);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)credentialId.Length);
        authData.AddRange(length);
        authData.AddRange(credentialId);
        authData.AddRange(CoseKey(signer));

        var attestation = new CborWriter(CborConformanceMode.Lax);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString([.. authData]);
        attestation.WriteEndMap();

        var clientData = ClientData("webauthn.create", options.Challenge, request.Origin);
        return new PasskeyRegistrationCredential(
            id, id, "public-key",
            new PasskeyAttestationResponse(Base64Url.EncodeToString(clientData), Base64Url.EncodeToString(attestation.Encode()), ["internal"]),
            "platform");
    }

    /// <summary>Answers <c>navigator.credentials.get</c> with a passkey this authenticator made; null for any other ID.</summary>
    public PasskeyAssertionCredential? Assert(TestCreatePasskeyAssertionRequest request)
    {
        if (!keys.TryGetValue(request.CredentialId, out var key)) return null;

        var eligible = key.BackupEligible || (request.BackedUp ?? false);
        var flags = (byte)(UserPresent | (request.UserVerified ?? true ? UserVerified : 0)
            | (eligible ? BackupEligible : 0) | (request.BackedUp ?? key.BackupEligible ? BackedUp : 0));
        var authData = AuthDataHead(request.RpId ?? request.Options.RpId, flags, (uint)(request.SignCount ?? 0));
        var clientData = ClientData("webauthn.get", request.Options.Challenge, request.Origin);
        var signature = key.Signer.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new PasskeyAssertionCredential(
            request.CredentialId, request.CredentialId, "public-key",
            new PasskeyAssertionResponse(
                Base64Url.EncodeToString(clientData),
                Base64Url.EncodeToString(authData),
                Base64Url.EncodeToString(signature),
                request.IncludeUserHandle ?? true ? Base64Url.EncodeToString(key.UserHandle) : null),
            "platform");
    }

    private static byte[] AuthDataHead(string rpId, byte flags, uint signCount)
    {
        var head = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(head, 0);
        head[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(33), signCount);
        return head;
    }

    /// <summary>The public key as a COSE EC2 key: kty 2, alg -7 (ES256), crv 1 (P-256), x, y.</summary>
    private static byte[] CoseKey(ECDsa signer)
    {
        var point = signer.ExportParameters(includePrivateParameters: false).Q;
        var cose = new CborWriter(CborConformanceMode.Lax);
        cose.WriteStartMap(5);
        cose.WriteInt32(1);
        cose.WriteInt32(2);
        cose.WriteInt32(3);
        cose.WriteInt32(-7);
        cose.WriteInt32(-1);
        cose.WriteInt32(1);
        cose.WriteInt32(-2);
        cose.WriteByteString(point.X!);
        cose.WriteInt32(-3);
        cose.WriteByteString(point.Y!);
        cose.WriteEndMap();
        return cose.Encode();
    }

    private static byte[] ClientData(string type, string challenge, string origin)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString("challenge", challenge);
            w.WriteString("origin", origin);
            w.WriteBoolean("crossOrigin", false);
            w.WriteEndObject();
        }

        return buffer.ToArray();
    }
}
