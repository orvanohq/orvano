using System.Security.Cryptography;
using System.Text;
using Orvano.Core;
using Orvano.Core.Secrets;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Core;

// Spec 0002, secrets at rest; spec 0004 AC-34 (refresh tokens and signing keys are stored envelope encrypted).
public class SecretBoxTests(PostgresFixture postgres)
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Repeat((byte)0xA1, 32).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Repeat((byte)0xB2, 32).ToArray());
    private static readonly string Row = SecretBox.AssociatedData("auth_sessions", "0198c0de-0000-7000-8000-000000000001", "refresh_ciphertext");

    [Fact]
    public void Round_trips_a_secret_for_the_same_row_and_column()
    {
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));

        var blob = box.Encrypt("orv_rt_secret"u8, Row);

        Assert.Equal("orv_rt_secret", Encoding.UTF8.GetString(box.Decrypt(blob, Row)));
        Assert.DoesNotContain("orv_rt_secret", Encoding.UTF8.GetString(blob), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_encryptions_of_one_value_differ()
    {
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));

        Assert.NotEqual(box.Encrypt("same"u8, Row), box.Encrypt("same"u8, Row));
    }

    [Fact]
    public void Refuses_a_ciphertext_moved_to_another_row()
    {
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));
        var blob = box.Encrypt("value"u8, Row);

        Assert.Throws<SecretBoxException>(() =>
            box.Decrypt(blob, SecretBox.AssociatedData("auth_sessions", "0198c0de-0000-7000-8000-000000000002", "refresh_ciphertext")));
    }

    [Fact]
    public void Refuses_a_tampered_blob()
    {
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));
        var blob = box.Encrypt("value"u8, Row);
        blob[^1] ^= 0x01;

        Assert.Throws<SecretBoxException>(() => box.Decrypt(blob, Row));
    }

    [Fact]
    public void Refuses_a_truncated_or_unknown_format()
    {
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));
        var blob = box.Encrypt("value"u8, Row);

        Assert.Throws<SecretBoxException>(() => box.Decrypt(blob.AsSpan(0, 20), Row));
        blob[0] = 9;
        Assert.Throws<SecretBoxException>(() => box.Decrypt(blob, Row));
    }

    [Fact]
    public void A_rotated_key_list_still_opens_old_secrets_and_seals_with_the_new_key()
    {
        var old = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));
        var sealedByOld = old.Encrypt("old"u8, Row);

        var rotated = MasterKeys.Parse($"kb:{KeyB},ka:{KeyA}");
        var box = new SecretBox(rotated);

        Assert.Equal("kb", rotated.ActiveId);
        Assert.Equal("old", Encoding.UTF8.GetString(box.Decrypt(sealedByOld, Row)));
        Assert.Throws<SecretBoxException>(() => old.Decrypt(box.Encrypt("new"u8, Row), Row));
    }

    [Fact]
    public void Refuses_a_secret_whose_master_key_was_removed()
    {
        var blob = new SecretBox(MasterKeys.Parse($"ka:{KeyA}")).Encrypt("value"u8, Row);

        var error = Assert.Throws<SecretBoxException>(() => new SecretBox(MasterKeys.Parse($"kb:{KeyB}")).Decrypt(blob, Row));
        Assert.Contains("no longer configured", error.Message);
    }

    [Fact]
    public void Refuses_the_same_id_with_another_key()
    {
        var blob = new SecretBox(MasterKeys.Parse($"ka:{KeyA}")).Encrypt("value"u8, Row);

        Assert.Throws<SecretBoxException>(() => new SecretBox(MasterKeys.Parse($"ka:{KeyB}")).Decrypt(blob, Row));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nokey")]
    [InlineData(":AAAA")]
    [InlineData("ka:not base64")]
    [InlineData("ka:AAAA")]
    [InlineData("bad id!:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Refuses_a_malformed_setting(string value)
    {
        var error = Assert.Throws<OrvanoConfigException>(() => MasterKeys.Parse(value));

        Assert.StartsWith("ORVANO_MASTER_KEYS is invalid", error.Message);
    }

    [Fact]
    public void Refuses_repeated_ids_and_never_echoes_a_key()
    {
        var error = Assert.Throws<OrvanoConfigException>(() => MasterKeys.Parse($"ka:{KeyA},ka:{KeyB}"));

        Assert.Contains("'ka' appears twice", error.Message);
        Assert.DoesNotContain(KeyA, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyB, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_the_installer_format_and_the_url_safe_alphabet()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var urlSafe = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.Equal("k20260927", MasterKeys.Parse($"k20260927:{Convert.ToBase64String(bytes)}").ActiveId);
        Assert.Equal("k2", MasterKeys.Parse($" k2:{urlSafe} ").ActiveId);
    }

    [Fact]
    public async Task Round_trips_through_a_bytea_column()
    {
        await using var database = await postgres.NewDatabaseAsync();
        var box = new SecretBox(MasterKeys.Parse($"ka:{KeyA}"));
        var blob = box.Encrypt("stored secret"u8, Row);

        await TestDatabase.ExecuteAsync(database.Superuser, "CREATE TABLE secrets (id int PRIMARY KEY, value bytea NOT NULL)");
        await TestDatabase.ExecuteAsync(database.Superuser, "INSERT INTO secrets VALUES (1, @value)", ("value", blob));
        var read = await TestDatabase.ScalarAsync<byte[]>(database.Superuser, "SELECT value FROM secrets WHERE id = 1");

        Assert.Equal("stored secret", Encoding.UTF8.GetString(box.Decrypt(read, Row)));
    }
}
