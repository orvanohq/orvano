using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 build task 3 over HTTP against the real binary: the Passkeys settings and their RP ID and origin rules,
// registration, sign in with no email typed, a passkey as step two and for step up, rename and delete, the counter
// rule, inactive passkeys after an RP ID change, and the passkeys switch. AC-1 to AC-4, AC-11, AC-19 to AC-24, AC-30,
// AC-45. Every passkey comes from the Test only software authenticator (test.createPasskeyCredential and
// test.createPasskeyAssertion), so each ceremony is a real WebAuthn one.
public class PasskeyTests(PostgresFixture postgres)
{
    private const string Origin = "http://localhost:3000";
    private const string MethodsUrl = "/v1/console/project/auth/methods";

    [Fact]
    public async Task Settings_read_as_defaults_and_check_every_field()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);

        using var defaults = await api.AsConsoleAsync(HttpMethod.Get, MethodsUrl);
        Assert.Equal(HttpStatusCode.OK, defaults.Status);
        Assert.True(defaults.Body.GetProperty("totpEnabled").GetBoolean());
        Assert.False(defaults.Body.GetProperty("passkeysEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.Body.GetProperty("rpId").ValueKind);
        Assert.Equal(0, defaults.Body.GetProperty("acceptedOrigins").GetArrayLength());

        foreach (var bad in new object[]
        {
            new { passkeysEnabled = true },
            new { rpId = "Example.com" },
            new { rpId = "https://example.com" },
            new { rpId = "example.com:443" },
            new { rpId = "10.0.0.1" },
            new { rpId = "-x.example.com" },
            new { rpName = new string('x', 65) },
            new { androidCertFingerprints = new[] { "AB:CD" } },
        })
        {
            using var refused = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, bad);
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("invalid_request", refused.Code);
        }

        var fingerprint = string.Join(':', Enumerable.Range(0, 32).Select(i => i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl,
            new { passkeysEnabled = true, rpId = "example.com", rpName = "Acme", androidCertFingerprints = new[] { fingerprint } });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(fingerprint.ToUpperInvariant(), saved.Body.GetProperty("androidCertFingerprints")[0].GetString());
        var origins = saved.Body.GetProperty("acceptedOrigins").EnumerateArray().Select(o => o.GetString()).ToList();
        // app.example.com is a web platform under the RP ID; localhost is not.
        Assert.Contains("https://app.example.com", origins);
        Assert.Contains("https://example.com", origins);
        Assert.Contains(origins, o => o!.StartsWith("android:apk-key-hash:", StringComparison.Ordinal));
        Assert.DoesNotContain(origins, o => o!.Contains("localhost", StringComparison.Ordinal));
        Assert.Equal("androidCertFingerprints,passkeysEnabled,rpId,rpName", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            """
            SELECT string_agg(field, ',' ORDER BY field)
            FROM orvano.events, jsonb_array_elements_text(payload->'changed') AS field
            WHERE type = 'auth.method_settings.updated'
            """));

        // Left out keeps; null clears, which passkeys need, so it is refused while they are on.
        using var renamed = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { rpName = (string?)null });
        Assert.Equal(JsonValueKind.Null, renamed.Body.GetProperty("rpName").ValueKind);
        Assert.Equal("example.com", renamed.Body.GetProperty("rpId").GetString());
        using var cleared = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { rpId = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, cleared.Status);

        await api.GrantAsync("viewer@x.com", "viewer");
        using var viewerRead = await api.AsConsoleAsync(HttpMethod.Get, MethodsUrl, account: "viewer@x.com");
        Assert.Equal(HttpStatusCode.OK, viewerRead.Status);
        using var viewerWrite = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { totpEnabled = false }, account: "viewer@x.com");
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.Status);
        Assert.Equal("forbidden", viewerWrite.Code);
    }

    [Fact]
    public async Task A_registered_passkey_signs_in_with_no_email_typed_at_level_2_and_is_never_challenged()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        // MFA on: a passkey sign in still answers a session at once (AC-24).
        var enrolled = await EnrollAsync(api, "ada@x.com");
        using var strong = await api.SignInAsync("ada@x.com");
        using var stepTwo = await StepTwoAsync(api, Ticket(strong), totpCode: CodeAt(enrolled.Secret, +1));

        using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", new { }, bearer: AuthApi.AccessToken(stepTwo));
        Assert.Equal(HttpStatusCode.OK, options.Status);
        var creation = options.Body.GetProperty("options");
        Assert.Equal("localhost", creation.GetProperty("rp").GetProperty("id").GetString());
        Assert.Equal("Auth project", creation.GetProperty("rp").GetProperty("name").GetString());
        Assert.Equal("ada@x.com", creation.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal([-7, -8, -257], creation.GetProperty("pubKeyCredParams").EnumerateArray().Select(p => p.GetProperty("alg").GetInt32()));
        Assert.Equal("required", creation.GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        Assert.Equal("none", creation.GetProperty("attestation").GetString());
        Assert.Equal(300000, creation.GetProperty("timeout").GetInt32());

        var registered = await CompleteRegistrationAsync(api, AuthApi.AccessToken(stepTwo), options);
        Assert.Equal("Passkey", registered.Passkey.GetProperty("name").GetString());
        Assert.True(registered.Passkey.GetProperty("active").GetBoolean());
        Assert.False(registered.Passkey.GetProperty("synced").GetBoolean());

        using var signedIn = await PasskeySignInAsync(api, registered.CredentialId);
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
        Assert.Equal(JsonValueKind.Null, signedIn.Body.GetProperty("mfa").ValueKind);
        Assert.Equal(enrolled.UserId, AuthApi.UserId(signedIn));
        var claims = Claims(AuthApi.AccessToken(signedIn));
        Assert.Equal(2, claims.GetProperty("aal").GetInt32());
        Assert.Equal(["hwk", "mfa", "user"], claims.GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("passkey", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT method FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(signedIn)))));
        Assert.True(await TestDatabase.ScalarAsync<bool>(api.Database.Superuser,
            "SELECT strong_auth_at IS NOT NULL AND last_used_at IS NOT NULL FROM orvano.auth_sessions, orvano.auth_passkeys WHERE auth_sessions.id = @id",
            ("id", Guid.Parse(Sid(signedIn)))));
        Assert.Equal(1, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.passkey.added'"));
    }

    [Fact]
    public async Task Every_failed_passkey_sign_in_gets_the_same_body()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        var registered = await RegisterAsync(api, bearer);

        // A credential the authenticator made but nobody registered.
        using var stranger = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: await VerifiedUserAsync(api, "bob@x.com"));
        var unregistered = await CreateCredentialAsync(api, stranger.Body.GetProperty("options"));

        var cases = new (string Name, string CredentialId, object? Extra)[]
        {
            ("wrong origin", registered.CredentialId, new { origin = "http://evil.test:3000" }),
            ("wrong RP ID", registered.CredentialId, new { rpId = "example.com" }),
            ("no user verification", registered.CredentialId, new { userVerified = false }),
            ("unknown credential", unregistered.GetProperty("id").GetString()!, null),
        };
        foreach (var (name, credentialId, extra) in cases)
        {
            using var refused = await PasskeySignInAsync(api, credentialId, extra);
            Assert.True(refused.Status == HttpStatusCode.Unauthorized, name);
            Assert.Equal("invalid_passkey", refused.Code);
            Assert.Equal("The passkey could not be checked. Try again or use another way to sign in.", refused.Body.GetProperty("detail").GetString());
        }

        // A replayed challenge: the first answer spends it.
        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge");
        var assertion = await AssertAsync(api, challenge.Body.GetProperty("options"), registered.CredentialId);
        var body = new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = assertion };
        using var first = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey", body);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        using var replay = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey", body);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.Status);
        Assert.Equal("invalid_passkey", replay.Code);
    }

    [Fact]
    public async Task A_counter_that_goes_down_fails_and_is_recorded_while_a_synced_passkey_at_0_keeps_working()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        var counting = await RegisterAsync(api, bearer);
        var synced = await RegisterAsync(api, bearer, backedUp: true);

        using var five = await PasskeySignInAsync(api, counting.CredentialId, new { signCount = 5 });
        Assert.Equal(HttpStatusCode.Created, five.Status);
        using var three = await PasskeySignInAsync(api, counting.CredentialId, new { signCount = 3 });
        Assert.Equal(HttpStatusCode.Unauthorized, three.Status);
        using var same = await PasskeySignInAsync(api, counting.CredentialId, new { signCount = 5 });
        Assert.Equal(HttpStatusCode.Unauthorized, same.Status);
        // A clone that reset its counter to 0 is a regression too (AC-22), recorded like the others.
        using var reset = await PasskeySignInAsync(api, counting.CredentialId, new { signCount = 0 });
        Assert.Equal(HttpStatusCode.Unauthorized, reset.Status);
        Assert.Equal("invalid_passkey", reset.Code);
        Assert.Equal(5L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT sign_count FROM orvano.auth_passkeys WHERE id = @id::uuid", ("id", counting.Passkey.GetProperty("id").GetString()!)));
        Assert.Equal(3, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.passkey.counter_regressed' AND payload->>'passkeyId' = @id",
            ("id", counting.Passkey.GetProperty("id").GetString()!)));

        for (var i = 0; i < 2; i++)
        {
            using var zero = await PasskeySignInAsync(api, synced.CredentialId);
            Assert.Equal(HttpStatusCode.Created, zero.Status);
            Assert.Equal(["mfa", "swk", "user"], Claims(AuthApi.AccessToken(zero)).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        }
    }

    [Fact]
    public async Task A_passkey_answers_step_two_only_for_the_tickets_own_user()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await EnrollAsync(api, "ada@x.com");
        var bob = await EnrollAsync(api, "bob@x.com");
        var adaKey = await RegisterAsync(api, await StrongBearerAsync(api, "ada@x.com", ada.Secret));
        var bobKey = await RegisterAsync(api, await StrongBearerAsync(api, "bob@x.com", bob.Secret));

        using var stepOne = await api.SignInAsync("ada@x.com");
        Assert.Equal(["totp", "recovery_code", "passkey"], stepOne.Body.GetProperty("mfa").GetProperty("factors").EnumerateArray().Select(f => f.GetString()));
        var ticket = Ticket(stepOne);

        // Bob's passkey at Ada's step two is a wrong factor, counted against the ticket.
        using var forBob = await MfaChallengeAsync(api, ticket);
        Assert.Equal([adaKey.CredentialId], forBob.Body.GetProperty("options").GetProperty("allowCredentials").EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        using var wrong = await StepTwoWithPasskeyAsync(api, ticket, forBob, bobKey.CredentialId);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal("invalid_passkey", wrong.Code);
        Assert.Equal(1, await TestDatabase.ScalarAsync<short>(api.Database.Superuser, "SELECT attempts FROM orvano.auth_mfa_tickets"));

        using var forAda = await MfaChallengeAsync(api, ticket);
        using var right = await StepTwoWithPasskeyAsync(api, ticket, forAda, adaKey.CredentialId);
        Assert.Equal(HttpStatusCode.Created, right.Status);
        Assert.Equal(ada.UserId, AuthApi.UserId(right));
        Assert.Equal(["hwk", "mfa", "pwd", "user"], Claims(AuthApi.AccessToken(right)).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(0, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_mfa_tickets"));

        // An unknown ticket is refused before any challenge is made.
        using var gone = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa/passkey-challenge", new { ticket });
        Assert.Equal(HttpStatusCode.Unauthorized, gone.Status);
        Assert.Equal("invalid_mfa_ticket", gone.Code);
    }

    [Fact]
    public async Task A_passkey_step_up_raises_only_its_own_users_session()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await VerifiedUserAsync(api, "ada@x.com");
        var bob = await VerifiedUserAsync(api, "bob@x.com");
        var adaKey = await RegisterAsync(api, ada);
        await RegisterAsync(api, bob);
        using var bobSession = await api.SignInAsync("bob@x.com");
        var bobBearer = AuthApi.AccessToken(bobSession);
        await AgeSessionsAsync(api);

        // An old session without MFA can't remove a passkey until it steps up.
        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/passkeys", bearer: bobBearer);
        var bobPasskey = list.Body.GetProperty("items")[0].GetProperty("id").GetString();
        using var stale = await api.SendAsync(HttpMethod.Delete, $"/v1/account/passkeys/{bobPasskey}", bearer: bobBearer);
        Assert.Equal(HttpStatusCode.Forbidden, stale.Status);
        Assert.Equal("reauthentication_required", stale.Code);

        // Ada's passkey can't answer Bob's step up challenge.
        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: bobBearer);
        Assert.Equal(HttpStatusCode.OK, challenge.Status);
        var foreign = await AssertAsync(api, challenge.Body.GetProperty("options"), adaKey.CredentialId);
        using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify",
            new { passkey = new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = foreign } }, bearer: bobBearer);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal("invalid_passkey", refused.Code);
        Assert.Equal(1, await TestDatabase.ScalarAsync<short>(api.Database.Superuser,
            "SELECT aal FROM orvano.auth_sessions WHERE id = @id", ("id", Guid.Parse(Sid(bobSession)))));

        using var own = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: bobBearer);
        var bobAnswer = await AssertAsync(api, own.Body.GetProperty("options"), own.Body.GetProperty("options").GetProperty("allowCredentials")[0].GetProperty("id").GetString()!);
        using var verified = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/verify",
            new { passkey = new { challengeId = own.Body.GetProperty("challengeId").GetString(), credential = bobAnswer } }, bearer: bobBearer);
        Assert.Equal(HttpStatusCode.OK, verified.Status);
        var raised = verified.Body.GetProperty("accessToken").GetString()!;
        Assert.Equal(["hwk", "mfa", "pwd", "user"], Claims(raised).GetProperty("amr").EnumerateArray().Select(a => a.GetString()));

        using var removed = await api.SendAsync(HttpMethod.Delete, $"/v1/account/passkeys/{bobPasskey}", bearer: raised);
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        Assert.Equal("user", await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
            "SELECT payload->>'reason' FROM orvano.events WHERE type = 'auth.passkey.removed'"));
    }

    [Fact]
    public async Task Changing_the_rp_id_needs_a_confirm_and_turns_passkeys_inactive_until_it_changes_back()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        var registered = await RegisterAsync(api, bearer);

        using var unconfirmed = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { rpId = "example.com" });
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.Status);
        Assert.Equal("passkeys_exist", unconfirmed.Code);
        using var moved = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { rpId = "example.com", confirmRpIdChange = true });
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(0, moved.Body.GetProperty("activePasskeyCount").GetInt32());

        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/passkeys", bearer: bearer);
        Assert.False(list.Body.GetProperty("items")[0].GetProperty("active").GetBoolean());
        using var inactive = await PasskeySignInAsync(api, registered.CredentialId, new { rpId = "localhost" });
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.Status);

        using var back = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { rpId = "localhost" });
        Assert.Equal(1, back.Body.GetProperty("activePasskeyCount").GetInt32());
        using var active = await PasskeySignInAsync(api, registered.CredentialId);
        Assert.Equal(HttpStatusCode.Created, active.Status);
    }

    [Fact]
    public async Task With_passkeys_off_ceremonies_are_refused_but_rename_list_and_delete_still_work()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        var registered = await RegisterAsync(api, bearer);
        var id = registered.Passkey.GetProperty("id").GetString();
        using var off = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl, new { passkeysEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.Status);

        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge");
        Assert.Equal(HttpStatusCode.Conflict, challenge.Status);
        Assert.Equal("factor_not_enabled", challenge.Code);
        using var registration = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        Assert.Equal("factor_not_enabled", registration.Code);
        using var stepUp = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/passkey-challenge", bearer: bearer);
        Assert.Equal("factor_not_enabled", stepUp.Code);

        using var renamed = await api.SendAsync(HttpMethod.Patch, $"/v1/account/passkeys/{id}", new { name = "  MacBook  " }, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        Assert.Equal("MacBook", renamed.Body.GetProperty("name").GetString());
        using var blank = await api.SendAsync(HttpMethod.Patch, $"/v1/account/passkeys/{id}", new { name = " " }, bearer: bearer);
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);
        using var list = await api.SendAsync(HttpMethod.Get, "/v1/account/passkeys", bearer: bearer);
        Assert.Equal(1, list.Body.GetProperty("items").GetArrayLength());
        using var removed = await api.SendAsync(HttpMethod.Delete, $"/v1/account/passkeys/{id}", bearer: bearer);
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        using var again = await api.SendAsync(HttpMethod.Delete, $"/v1/account/passkeys/{id}", bearer: bearer);
        Assert.Equal(HttpStatusCode.NotFound, again.Status);
        Assert.Equal("passkey_not_found", again.Code);
    }

    [Fact]
    public async Task Registration_needs_a_verified_email_and_stops_at_10_passkeys()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var bearer = AuthApi.AccessToken(signUp);

        using var unverified = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        Assert.Equal(HttpStatusCode.Conflict, unverified.Status);
        Assert.Equal("email_not_verified", unverified.Code);

        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        var first = await RegisterAsync(api, bearer, name: "Phone");
        Assert.Equal("Phone", first.Passkey.GetProperty("name").GetString());

        // The same authenticator answering an old challenge's options again is refused: the challenge is spent.
        using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        Assert.Equal([first.CredentialId], options.Body.GetProperty("options").GetProperty("excludeCredentials").EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        var credential = await CreateCredentialAsync(api, options.Body.GetProperty("options"));
        var body = new { challengeId = options.Body.GetProperty("challengeId").GetString(), credential };
        using var made = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys", body, bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, made.Status);
        using var spent = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys", body, bearer: bearer);
        Assert.Equal(HttpStatusCode.BadRequest, spent.Status);
        Assert.Equal("invalid_passkey_challenge", spent.Code);

        // Eight more rows (the enrollment limit allows 10 calls per 15 minutes, so they are seeded), then the limit.
        await TestDatabase.ExecuteAsync(api.Database.Superuser, """
            INSERT INTO orvano.auth_passkeys (project_id, user_id, credential_id, public_key, sign_count, name, backup_eligible, backed_up, rp_id)
            SELECT project_id, id, uuid_send(gen_random_uuid()), '\x00', 0, 'Seeded', false, false, 'localhost'
            FROM orvano.auth_users, generate_series(1, 8) WHERE email = 'ada@x.com'
            """);
        using var full = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        Assert.Equal(HttpStatusCode.Conflict, full.Status);
        Assert.Equal("passkey_limit", full.Code);
    }

    // AC-21: a registration without the user verified flag, for another RP ID, on another user's register challenge, or
    // with a credential ID the project already has is refused, and nothing is stored.
    [Fact]
    public async Task Registration_refuses_no_user_verification_a_wrong_rp_id_another_users_challenge_and_a_known_credential()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var ada = await VerifiedUserAsync(api, "ada@x.com");
        var bob = await VerifiedUserAsync(api, "bob@x.com");

        foreach (var (name, userVerified, rpId) in new[] { ("no user verification", (bool?)false, (string?)null), ("wrong RP ID", null, "example.com") })
        {
            using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: ada);
            var credential = await CreateCredentialAsync(api, options.Body.GetProperty("options"), userVerified: userVerified, rpId: rpId);
            using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys",
                new { challengeId = options.Body.GetProperty("challengeId").GetString(), credential }, bearer: ada);
            Assert.True(refused.Status == HttpStatusCode.Unauthorized, name);
            Assert.Equal("invalid_passkey", refused.Code);
        }

        // Bob can't spend Ada's register challenge, and trying leaves it for Ada.
        using var adaOptions = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: ada);
        var adaCredential = await CreateCredentialAsync(api, adaOptions.Body.GetProperty("options"));
        var adaBody = new { challengeId = adaOptions.Body.GetProperty("challengeId").GetString(), credential = adaCredential };
        using var stolen = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys", adaBody, bearer: bob);
        Assert.Equal(HttpStatusCode.BadRequest, stolen.Status);
        Assert.Equal("invalid_passkey_challenge", stolen.Code);
        using var own = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys", adaBody, bearer: ada);
        Assert.Equal(HttpStatusCode.Created, own.Status);

        // A credential ID the project already has (here another user's row, seeded with the new credential's ID).
        using var bobOptions = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bob);
        var bobCredential = await CreateCredentialAsync(api, bobOptions.Body.GetProperty("options"));
        await TestDatabase.ExecuteAsync(api.Database.Superuser, """
            INSERT INTO orvano.auth_passkeys (project_id, user_id, credential_id, public_key, sign_count, name, backup_eligible, backed_up, rp_id)
            SELECT project_id, id, @credential, '\x00', 0, 'Seeded', false, false, 'localhost' FROM orvano.auth_users WHERE email = 'ada@x.com'
            """, ("credential", System.Buffers.Text.Base64Url.DecodeFromChars(bobCredential.GetProperty("id").GetString()!)));
        using var duplicate = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys",
            new { challengeId = bobOptions.Body.GetProperty("challengeId").GetString(), credential = bobCredential }, bearer: bob);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.Status);
        Assert.Equal("passkey_already_registered", duplicate.Code);

        Assert.Equal(2L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_passkeys"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.events WHERE type = 'auth.passkey.added'"));
    }

    [Fact]
    public async Task Origins_follow_the_rp_id_the_web_platforms_and_the_android_fingerprints()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        var fingerprint = string.Join(':', Enumerable.Repeat("AB", 32));
        await EnablePasskeysAsync(api, "example.com", [fingerprint]);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");

        // A web platform under the RP ID, https:// plus the RP ID (iOS and macOS), and the listed Android app pass.
        var web = await RegisterAsync(api, bearer, origin: "https://app.example.com");
        await RegisterAsync(api, bearer, origin: "https://example.com");
        var android = "android:apk-key-hash:" + Convert.ToBase64String(Enumerable.Repeat((byte)0xAB, 32).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await RegisterAsync(api, bearer, origin: android);

        foreach (var refusedOrigin in new[] { "https://other.example.com", "http://localhost:3000", "android:apk-key-hash:AAAA", "https://example.com:8443" })
        {
            using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
            var credential = await CreateCredentialAsync(api, options.Body.GetProperty("options"), refusedOrigin);
            using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys",
                new { challengeId = options.Body.GetProperty("challengeId").GetString(), credential }, bearer: bearer);
            Assert.True(refused.Status == HttpStatusCode.Unauthorized, refusedOrigin);
            Assert.Equal("invalid_passkey", refused.Code);
        }

        using var signedIn = await PasskeySignInAsync(api, web.CredentialId, new { origin = "https://app.example.com" });
        Assert.Equal(HttpStatusCode.Created, signedIn.Status);
    }

    [Fact]
    public async Task An_active_passkey_counts_as_a_way_in_when_unlinking_the_last_identity()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true);
        await EnablePasskeysAsync(api);
        var bearer = await VerifiedUserAsync(api, "ada@x.com");
        await RegisterAsync(api, bearer);
        var userId = await TestDatabase.ScalarAsync<Guid>(api.Database.Superuser, "SELECT id FROM orvano.auth_users WHERE email = 'ada@x.com'");
        await TestDatabase.ExecuteAsync(api.Database.Superuser, """
            DELETE FROM orvano.auth_passwords WHERE user_id = @id;
            UPDATE orvano.auth_users SET email = NULL, email_verified_at = NULL WHERE id = @id;
            INSERT INTO orvano.auth_identities (project_id, user_id, provider, subject) VALUES ('authproject0001', @id, 'github', 'gh-1');
            """, ("id", userId));
        var identity = await TestDatabase.ScalarAsync<Guid>(api.Database.Superuser, "SELECT id FROM orvano.auth_identities");

        using var unlinked = await api.AsServerAsync(HttpMethod.Delete, $"/v1/users/{userId}/identities/{identity}");
        Assert.Equal(HttpStatusCode.NoContent, unlinked.Status);
    }

    internal sealed record Registered(string CredentialId, JsonElement Passkey);

    /// <summary>The body of an enrollment call by a user with a password and no MFA (AC-17).</summary>
    internal static readonly object WithPassword = new { password = Password };

    internal static async Task EnablePasskeysAsync(AuthApi api, string rpId = "localhost", string[]? fingerprints = null)
    {
        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, MethodsUrl,
            new { passkeysEnabled = true, rpId, androidCertFingerprints = fingerprints ?? [] });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
    }

    /// <summary>A new user with a verified email and a fresh session; answers its access token.</summary>
    internal static async Task<string> VerifiedUserAsync(AuthApi api, string email)
    {
        using var signUp = await api.SignUpAsync(email);
        await VerifyEmailAsync(api, AuthApi.UserId(signUp));
        return AuthApi.AccessToken(signUp);
    }

    /// <summary>
    /// A session of a user just enrolled by <see cref="MfaTests.EnrollAsync"/> that passed its second step now, with the
    /// next step's code (the confirm used this one's).
    /// </summary>
    internal static async Task<string> StrongBearerAsync(AuthApi api, string email, string secret)
    {
        using var stepOne = await api.SignInAsync(email);
        using var stepTwo = await StepTwoAsync(api, Ticket(stepOne), totpCode: CodeAt(secret, +1));
        Assert.Equal(HttpStatusCode.Created, stepTwo.Status);
        return AuthApi.AccessToken(stepTwo);
    }

    internal static async Task<Registered> RegisterAsync(AuthApi api, string bearer, string origin = Origin, bool backedUp = false, string? name = null)
    {
        using var options = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys/registration", WithPassword, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, options.Status);
        return await CompleteRegistrationAsync(api, bearer, options, origin, backedUp, name);
    }

    private static async Task<Registered> CompleteRegistrationAsync(
        AuthApi api, string bearer, Reply options, string origin = Origin, bool backedUp = false, string? name = null)
    {
        var credential = await CreateCredentialAsync(api, options.Body.GetProperty("options"), origin, backedUp);
        using var made = await api.SendAsync(HttpMethod.Post, "/v1/account/passkeys",
            new Dictionary<string, object?> { ["challengeId"] = options.Body.GetProperty("challengeId").GetString(), ["credential"] = credential, ["name"] = name }
                .Where(p => p.Value is not null).ToDictionary(), bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, made.Status);
        return new Registered(credential.GetProperty("id").GetString()!, made.Body.Clone());
    }

    /// <summary>
    /// The software authenticator's new passkey for <paramref name="options"/>; <paramref name="userVerified"/> false
    /// drops the user verified flag, and <paramref name="rpId"/> hashes another RP ID into the authenticator data.
    /// </summary>
    internal static async Task<JsonElement> CreateCredentialAsync(
        AuthApi api, JsonElement options, string origin = Origin, bool backedUp = false, bool? userVerified = null, string? rpId = null)
    {
        var body = new Dictionary<string, object?> { ["options"] = options, ["origin"] = origin, ["backedUp"] = backedUp, ["userVerified"] = userVerified, ["rpId"] = rpId }
            .Where(p => p.Value is not null).ToDictionary();
        using var made = await api.SendAsync(HttpMethod.Post, "/v1/test/passkeys/credentials", body);
        Assert.Equal(HttpStatusCode.OK, made.Status);
        return made.Body.Clone();
    }

    /// <summary>The software authenticator's answer; <paramref name="extra"/> overrides fields such as origin or signCount.</summary>
    internal static async Task<JsonElement> AssertAsync(AuthApi api, JsonElement options, string credentialId, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["options"] = options, ["origin"] = Origin, ["credentialId"] = credentialId };
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject()) body[property.Name] = property.Value;
        }

        using var made = await api.SendAsync(HttpMethod.Post, "/v1/test/passkeys/assertions", body);
        Assert.Equal(HttpStatusCode.OK, made.Status);
        return made.Body.Clone();
    }

    internal static async Task<Reply> PasskeySignInAsync(AuthApi api, string credentialId, object? extra = null)
    {
        using var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey-challenge");
        Assert.Equal(HttpStatusCode.OK, challenge.Status);
        var assertion = await AssertAsync(api, challenge.Body.GetProperty("options"), credentialId, extra);
        return await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/passkey",
            new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = assertion });
    }

    internal static async Task<Reply> MfaChallengeAsync(AuthApi api, string ticket)
    {
        var challenge = await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa/passkey-challenge", new { ticket });
        Assert.Equal(HttpStatusCode.OK, challenge.Status);
        return challenge;
    }

    internal static async Task<Reply> StepTwoWithPasskeyAsync(AuthApi api, string ticket, Reply challenge, string credentialId)
    {
        var assertion = await AssertAsync(api, challenge.Body.GetProperty("options"), credentialId);
        return await api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa",
            new { ticket, passkey = new { challengeId = challenge.Body.GetProperty("challengeId").GetString(), credential = assertion } });
    }

    /// <summary>Moves every session's creation and strong check 11 minutes back, past the 10 minute window.</summary>
    private static Task AgeSessionsAsync(AuthApi api) =>
        TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET created_at = created_at - interval '11 minutes', strong_auth_at = strong_auth_at - interval '11 minutes'");
}
