using System.Diagnostics;
using System.Globalization;

using Orvano.Testing;

using Testcontainers.PostgreSql;

namespace Orvano.Host.IntegrationTests.Cli;

// Spec 0007 RUN-1, RUN-2, RUN-5, INS-2, TLS-2, IMG-4 and spec 0002 CFG-3, CFG-4, run as the real process.
public sealed class ServeAndMigrateTests(PostgresFixture postgres)
{
    [Fact, Trait("Spec", "0007:INS-2")]
    public async Task Migrate_exits_0_when_the_platform_database_is_PostgreSQL_18()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], postgres.DatabaseEnvironment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.Error);
    }

    [Fact, Trait("Spec", "0007:RUN-5")]
    public async Task Migrate_exits_1_naming_the_problem_when_the_database_is_unreachable()
    {
        var environment = new Dictionary<string, string>(postgres.DatabaseEnvironment)
        {
            ["ORVANO_DATABASE__HOST"] = "127.0.0.1",
            ["ORVANO_DATABASE__PORT"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        };

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], environment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("Cannot reach the platform database");
        (result.Output + result.Error).ShouldNotContain(PostgresFixture.Password);
    }

    [Fact, Trait("Spec", "0002:CFG-4")]
    public async Task Migrate_exits_1_naming_the_variable_when_the_database_username_is_missing()
    {
        var environment = new Dictionary<string, string>(postgres.DatabaseEnvironment);
        environment.Remove("ORVANO_DATABASE__USERNAME");

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], environment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_DATABASE__USERNAME");
    }

    [Fact, Trait("Spec", "0002:CFG-3")]
    public async Task Migrate_reads_the_password_from_a_file_when_PASSWORD_FILE_is_set()
    {
        string secret = Path.GetTempFileName();
        await File.WriteAllTextAsync(secret, PostgresFixture.Password + "\n", TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string>(postgres.DatabaseEnvironment) { ["ORVANO_DATABASE__PASSWORD_FILE"] = secret };
        environment.Remove("ORVANO_DATABASE__PASSWORD");

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], environment, TestContext.Current.CancellationToken);

        File.Delete(secret);
        result.ExitCode.ShouldBe(0, result.Error);
    }

    [Fact, Trait("Spec", "0007:RUN-5")]
    public async Task Migrate_refuses_when_the_platform_database_is_PostgreSQL_17()
    {
        await using PostgreSqlContainer old = new PostgreSqlBuilder("postgres:17.11").WithUsername("orvano").WithPassword("old-test").Build();
        await old.StartAsync(TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string>
        {
            ["ORVANO_DATABASE__HOST"] = old.Hostname,
            ["ORVANO_DATABASE__PORT"] = old.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort).ToString(CultureInfo.InvariantCulture),
            ["ORVANO_DATABASE__USERNAME"] = "orvano",
            ["ORVANO_DATABASE__PASSWORD"] = "old-test",
            ["ORVANO_DATABASE__NAME"] = "postgres",
            ["ORVANO_DATABASE__SSLMODE"] = "Disable",
        };

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], environment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("PostgreSQL 18");
    }

    [Fact, Trait("Spec", "0006:SCR-2")]
    public async Task Serve_exits_1_naming_the_variable_when_no_root_key_is_set()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], postgres.DatabaseEnvironment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_ROOT_KEY_FILE");
    }

    [Theory, Trait("Spec", "0007:RUN-2")]
    [InlineData("foo")]
    [InlineData("api,queue")]
    [InlineData(" , ")]
    public async Task Serve_exits_1_naming_ORVANO_ROLES_when_the_roles_are_not_valid(string roles)
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new() { ["ORVANO_ROLES"] = roles }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_ROLES");
    }

    [Fact, Trait("Spec", "0006:NET-1")]
    public async Task Serve_exits_1_naming_the_variable_when_a_trusted_proxy_is_not_an_address()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new() { ["ORVANO_TRUSTED_PROXIES"] = "10.0.0.1,caddy" }),
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_TRUSTED_PROXIES");
        result.Error.ShouldContain("caddy");
    }

    [Fact, Trait("Spec", "0007:TLS-2")]
    public async Task Serve_exits_1_naming_both_variables_when_only_the_certificate_is_set()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new() { ["ORVANO_TLS__CERTFILE"] = "/tls/cert.pem" }),
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_TLS__CERTFILE");
        result.Error.ShouldContain("ORVANO_TLS__KEYFILE");
    }

    [Fact, Trait("Spec", "0002:CFG-3")]
    public async Task Serve_exits_1_when_both_a_key_and_its_FILE_form_are_set()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new() { ["ORVANO_ROOT_KEY_FILE"] = "/run/secrets/root.key" }),
            TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("not both");
    }

    [Fact, Trait("Spec", "0007:RUN-5")]
    public async Task Serve_exits_1_naming_the_problem_when_the_database_is_unreachable()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["ORVANO_DATABASE__HOST"] = "127.0.0.1",
            ["ORVANO_DATABASE__PORT"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("Cannot reach the platform database");
    }

    [Fact, Trait("Spec", "0002:CFG-4")]
    public async Task Serve_exits_1_naming_the_variable_when_the_database_host_is_missing()
    {
        Dictionary<string, string> environment = ServeEnvironment(new());
        environment.Remove("ORVANO_DATABASE__HOST");

        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], environment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_DATABASE__HOST");
    }

    [Fact, Trait("Spec", "0013:AC-11"), Trait("Spec", "0013:SOP-2")]
    public async Task Serve_exits_1_naming_the_variable_when_the_heartbeat_timeout_is_under_twice_the_interval()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["ORVANO_SIGNALS__HEARTBEATTIMEOUT"] = "00:00:30",
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_SIGNALS__HEARTBEATTIMEOUT");
    }

    [Fact, Trait("Spec", "0013:AC-11"), Trait("Spec", "0013:SOP-2")]
    public async Task Serve_exits_1_naming_the_variable_when_the_handler_timeout_is_above_the_heartbeat_timeout()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["ORVANO_SIGNALS__HANDLERTIMEOUT"] = "00:01:31",
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("ORVANO_SIGNALS__HANDLERTIMEOUT");
    }

    [Fact, Trait("Spec", "0007:IMG-4")]
    public async Task Serve_boots_and_healthcheck_exits_0_when_the_database_is_ready()
    {
        string port = Ports.Free().ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string> environment = ServeEnvironment(new() { ["ASPNETCORE_HTTP_PORTS"] = port });
        using Process server = Process.Start(OrvanoCli.StartInfo(["serve"], environment)) ?? throw new InvalidOperationException("serve did not start");
        try
        {
            int exitCode = 1;
            for (int attempt = 0; attempt < 60 && exitCode != 0 && !server.HasExited; attempt++)
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
                exitCode = (await OrvanoCli.RunAsync(["healthcheck", "--port", port], null, TestContext.Current.CancellationToken)).ExitCode;
            }

            exitCode.ShouldBe(0, server.HasExited ? await server.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken) : "never ready");
        }
        finally
        {
            if (!server.HasExited)
            {
                server.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact, Trait("Spec", "0012:EPT-4"), Trait("Spec", "0012:AC-13")]
    public async Task Serve_exits_1_naming_the_endpoint_without_a_stack_trace_when_an_endpoint_declares_no_access()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["DOTNET_STARTUP_HOOKS"] = OrvanoCli.UndeclaredEndpointHook,
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1, result.Error);
        result.Error.ShouldContain("Every endpoint must declare its access");
        result.Error.ShouldContain("/test/undeclared: it declares no access");
        result.Error.ShouldNotContain(" at "); // no stack trace frames
    }

    [Theory]
    [Trait("Spec", "0013:AC-24"), Trait("Spec", "0013:SCH-1"), Trait("Spec", "0013:JQ-5")]
    [InlineData(BadJobRegistrations.BadName, "The job schedule name 'nightly' is not valid")]
    [InlineData(BadJobRegistrations.BadCron, "The job schedule hook.schedule has a cron expression that is not valid")]
    [InlineData(BadJobRegistrations.NeverFires, "The job schedule hook.schedule has the cron expression '0 0 30 2 *', which never fires")]
    [InlineData(BadJobRegistrations.Duplicate, "The job schedule hook.schedule is registered more than once")]
    [InlineData(BadJobRegistrations.UnknownKind, "The job schedule hook.schedule runs kind hook.job, which has no registered handler")]
    [InlineData(BadJobRegistrations.LargePayload, "The job schedule hook.schedule has a payload over 65536 bytes")]
    public async Task Serve_exits_1_naming_the_schedule_without_a_stack_trace_when_a_job_registration_is_bad(string registration, string message)
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["DOTNET_STARTUP_HOOKS"] = OrvanoCli.UndeclaredEndpointHook,
            [OrvanoCli.JobsHookVariable] = registration,
            ["ORVANO_ROLES"] = "api", // the check runs in every role, not only where the runner does
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1, result.Error);
        result.Error.ShouldContain(message);
        result.Error.ShouldNotContain(" at "); // no stack trace frames
    }

    [Theory]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-1"), Trait("Spec", "0013:JOP-2"), Trait("Spec", "0013:AC-40")]
    [InlineData(
        "ORVANO_JOBS__CONCURRENCY",
        "14",
        "ORVANO_JOBS__CONCURRENCY (14) must be at most 13: half of ORVANO_DATABASE__POOLSIZE (30) less two connections for the job runner and the outbox dispatcher")]
    [InlineData("ORVANO_JOBS__CONCURRENCY", "0", "ORVANO_JOBS__CONCURRENCY")]
    [InlineData("ORVANO_JOBS__POLLINTERVAL", "00:00:00", "ORVANO_JOBS__POLLINTERVAL")]
    [InlineData("ORVANO_DATABASE__POOLSIZE", "5", "ORVANO_DATABASE__POOLSIZE")] // at least 6, so the default concurrency passes JOP-2 (OPL-1)
    public async Task Serve_exits_1_naming_the_variable_when_a_job_option_is_out_of_range(string variable, string value, string message)
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["ORVANO_DATABASE__POOLSIZE"] = "30",
            [variable] = value,
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1, result.Error);
        result.Error.ShouldContain(message);
    }

    [Fact]
    [Trait("Spec", "0013:AC-26"), Trait("Spec", "0013:JOP-3"), Trait("Spec", "0013:AC-42"), Trait("Spec", "0013:DSP-1")]
    public async Task Migrate_runs_no_job_runner_and_no_outbox_dispatcher()
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["migrate"], postgres.DatabaseEnvironment, TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.Error);
        (result.Output + result.Error).ShouldNotContain("Job runner started");
        (result.Output + result.Error).ShouldNotContain("Outbox dispatcher started");
    }

    [Theory]
    [Trait("Spec", "0013:AC-35"), Trait("Spec", "0013:OBX-7")]
    [InlineData(BadOutboxRegistrations.SchemaTwice, "The outbox of schema sample is registered more than once")]
    [InlineData(BadOutboxRegistrations.TypeInTwoOutboxes, "The event type sample.note.added.v1 is registered by the sample and sample outboxes")]
    [InlineData(
        BadOutboxRegistrations.UnregisteredType,
        "The event subscriber hook.subscriber subscribes to sample.note.added.v1, which no outbox registers")]
    [InlineData(
        BadOutboxRegistrations.OtherClrType,
        "The event subscriber hook.subscriber reads sample.note.added.v1 as LookalikeNoteAdded, but its outbox registers it as SampleNoteAdded")]
    [InlineData(BadOutboxRegistrations.NamedLikeAKind, "The job kind hook.job is registered more than once")]
    public async Task Serve_exits_1_naming_the_registration_without_a_stack_trace_when_an_outbox_registration_is_bad(string registration, string message)
    {
        OrvanoCli.Result result = await OrvanoCli.RunAsync(["serve"], ServeEnvironment(new()
        {
            ["DOTNET_STARTUP_HOOKS"] = OrvanoCli.UndeclaredEndpointHook,
            [OrvanoCli.OutboxHookVariable] = registration,
            ["ORVANO_ROLES"] = "api", // the check runs in every role, not only where the dispatcher does
            ["ASPNETCORE_HTTP_PORTS"] = Ports.Free().ToString(CultureInfo.InvariantCulture),
        }), TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(1, result.Error);
        result.Error.ShouldContain(message);
        result.Error.ShouldNotContain(" at "); // no stack trace frames
    }

    private Dictionary<string, string> ServeEnvironment(Dictionary<string, string> extra)
    {
        var environment = new Dictionary<string, string>(postgres.DatabaseEnvironment) { ["ORVANO_ROOT_KEY"] = TestRootKey.Create() };
        foreach ((string? key, string? value) in extra)
        {
            environment[key] = value;
        }

        return environment;
    }
}
