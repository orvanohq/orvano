using Orvano.Server.Install;

namespace Orvano.Server.Tests.Install;

// Spec 0006: the .env a run writes (AC-8, AC-9, AC-11) and the decisions before it (AC-13, AC-18).
public class InstallPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static InstallInputs Inputs(string version = "0.1.0", string domain = "orvano.example.com", string email = "", long mem = 4096) =>
        new(version, domain, email, mem, Now);

    [Fact]
    public void A_fresh_install_writes_every_key_in_the_install_layout()
    {
        var result = InstallPlan.Apply(null, Inputs(email: "ops@example.com"));
        var env = result.Env;

        Assert.Equal("0.1.0", env.Get("ORVANO_VERSION"));
        Assert.Equal("https://orvano.example.com", env.Get("ORVANO_PUBLIC_URL"));
        Assert.Equal("ops@example.com", env.Get("ORVANO_ACME_EMAIL"));
        foreach (var key in InstallPlan.DatabasePasswords) Assert.Matches("^[0-9a-f]{48}$", env.Get(key));
        Assert.StartsWith("k20260926:", env.Get("ORVANO_MASTER_KEYS"));
        Assert.True(InstallSecrets.IsSetupToken(env.Get("ORVANO_SETUP_TOKEN")!));
        Assert.Equal("512MB", env.Get("ORVANO_PG_SHARED_BUFFERS"));
        Assert.Equal("1280M", env.Get("ORVANO_PG_MEMORY_LIMIT"));
        Assert.Equal(InstallPlan.Secrets, result.Generated);
        Assert.True(result.GeneratedMasterKey);
        Assert.True(result.EnvChanged);
        Assert.Null(result.PreviousEnv);
        Assert.False(result.PublicUrlChanged);
        Assert.StartsWith("# Orvano settings, written by the installer", env.ToString());
    }

    [Fact]
    public void A_rerun_keeps_every_secret_custom_key_comment_and_line_order()
    {
        var first = InstallPlan.Apply(null, Inputs()).Env.ToString();
        var edited = "# my own note\n" + first + "\nMY_KEY=kept # trailing\n\n# end\n";

        var result = InstallPlan.Apply(EnvFile.Parse(edited), Inputs());

        Assert.Empty(result.Generated);
        Assert.False(result.GeneratedMasterKey);
        Assert.Equal(edited, result.Env.ToString());
        Assert.False(result.EnvChanged);
    }

    [Fact]
    public void An_upgrade_changes_only_the_managed_keys_in_place()
    {
        var first = InstallPlan.Apply(null, Inputs(mem: 2048)).Env;
        var before = first.ToString();

        var result = InstallPlan.Apply(EnvFile.Parse(before), Inputs(version: "0.2.0", mem: 4096));
        var after = result.Env.ToString();

        Assert.True(result.EnvChanged);
        Assert.Equal(before, result.PreviousEnv);
        Assert.Equal(before.Split('\n').Length, after.Split('\n').Length);
        Assert.Equal("0.2.0", result.Env.Get("ORVANO_VERSION"));
        Assert.Equal("512MB", result.Env.Get("ORVANO_PG_SHARED_BUFFERS"));
        foreach (var key in InstallPlan.Secrets) Assert.Equal(first.Get(key), result.Env.Get(key));
    }

    [Fact]
    public void A_hand_written_env_keeps_its_values_and_gets_the_missing_ones()
    {
        var written = "POSTGRES_PASSWORD=mine\nORVANO_MASTER_KEYS=\nORVANO_PG_TUNING=manual\nORVANO_PG_SHARED_BUFFERS=2GB\n";

        var result = InstallPlan.Apply(EnvFile.Parse(written), Inputs());

        Assert.Equal("mine", result.Env.Get("POSTGRES_PASSWORD"));
        Assert.Equal(["ORVANO_ADMIN_PASSWORD", "ORVANO_APP_PASSWORD", "ORVANO_MASTER_KEYS", "ORVANO_SETUP_TOKEN"], result.Generated);
        Assert.True(result.ManualTuning);
        Assert.Equal("2GB", result.Env.Get("ORVANO_PG_SHARED_BUFFERS"));
        Assert.Null(result.Env.Get("ORVANO_PG_WORK_MEM"));
        Assert.StartsWith("POSTGRES_PASSWORD=mine\nORVANO_MASTER_KEYS=k20260926:", result.Env.ToString());
    }

    [Fact]
    public void A_changed_domain_on_an_existing_install_is_flagged()
    {
        var env = InstallPlan.Apply(null, Inputs()).Env.ToString();

        Assert.True(InstallPlan.Apply(EnvFile.Parse(env), Inputs(domain: "localhost")).PublicUrlChanged);
        Assert.False(InstallPlan.Apply(EnvFile.Parse(env), Inputs()).PublicUrlChanged);
        // A hand written .env without ORVANO_VERSION is a fresh install: no sessions to lose.
        Assert.False(InstallPlan.Apply(EnvFile.Parse("ORVANO_PUBLIC_URL=http://localhost\n"), Inputs()).PublicUrlChanged);
    }

    [Fact]
    public void Decide_refuses_a_missing_database_password_when_the_data_volume_exists()
    {
        var env = EnvFile.Parse("ORVANO_VERSION=0.1.0\nPOSTGRES_PASSWORD=a\nORVANO_ADMIN_PASSWORD=b\n");

        var (_, refusal) = InstallPlan.Decide(env, "0.1.0", existingData: true);

        Assert.Equal(
            "The Orvano database already exists but ORVANO_APP_PASSWORD is missing from .env. A new password would not match the database; restore .env from your backup.",
            refusal);
        Assert.Null(InstallPlan.Decide(env, "0.1.0", existingData: false).Refusal);
    }

    [Fact]
    public void Decide_applies_the_version_rule()
    {
        var env = EnvFile.Parse("ORVANO_VERSION=0.9.3\n");

        Assert.Equal((InstallMode.Upgrade, (string?)null), InstallPlan.Decide(env, "0.10.0", existingData: false));
        Assert.Equal((InstallMode.Fresh, (string?)null), InstallPlan.Decide(null, "0.1.0", existingData: false));
        Assert.NotNull(InstallPlan.Decide(env, "0.9.2", existingData: false).Refusal);
    }
}
