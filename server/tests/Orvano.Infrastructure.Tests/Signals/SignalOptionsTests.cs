using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Signals;
using Orvano.SharedKernel.Configuration;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Signals;

// Spec 0013 SOP-1, SOP-2, AC-11.
public sealed class SignalOptionsTests
{
    [Fact]
    [Trait("Spec", "0013:SOP-1")]
    public void Defaults_are_30_90_30_and_5_seconds_when_nothing_is_set()
    {
        SignalOptions options = Resolve(new());

        options.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(30));
        options.HeartbeatTimeout.ShouldBe(TimeSpan.FromSeconds(90));
        options.ReconnectBackoffCap.ShouldBe(TimeSpan.FromSeconds(30));
        options.FallbackPollInterval.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [Trait("Spec", "0013:SOP-2"), Trait("Spec", "0013:AC-11")]
    [InlineData("HeartbeatInterval", "ORVANO_SIGNALS__HEARTBEATINTERVAL")]
    [InlineData("HeartbeatTimeout", "ORVANO_SIGNALS__HEARTBEATTIMEOUT")]
    [InlineData("ReconnectBackoffCap", "ORVANO_SIGNALS__RECONNECTBACKOFFCAP")]
    [InlineData("FallbackPollInterval", "ORVANO_SIGNALS__FALLBACKPOLLINTERVAL")]
    public void Validation_fails_naming_the_variable_when_a_duration_is_under_1_second(string key, string variable)
    {
        OptionsValidationException error = Should.Throw<OptionsValidationException>(
            () => Resolve(new() { [$"Signals:{key}"] = "00:00:00.500" }));

        string.Join('\n', ConfigurationNames.Describe(error, SignalOptions.Section)).ShouldContain(variable);
    }

    [Fact]
    [Trait("Spec", "0013:SOP-2"), Trait("Spec", "0013:AC-11")]
    public void Validation_fails_naming_the_timeout_when_it_is_under_twice_the_interval()
    {
        OptionsValidationException error = Should.Throw<OptionsValidationException>(
            () => Resolve(new() { ["Signals:HeartbeatTimeout"] = "00:00:30" }));

        IReadOnlyList<string> lines = ConfigurationNames.Describe(error, SignalOptions.Section);
        lines.ShouldHaveSingleItem().ShouldStartWith("ORVANO_SIGNALS__HEARTBEATTIMEOUT");
    }

    [Fact]
    [Trait("Spec", "0013:SOP-2")]
    public void Validation_passes_when_the_timeout_is_exactly_twice_the_interval() =>
        Resolve(new() { ["Signals:HeartbeatInterval"] = "00:00:01", ["Signals:HeartbeatTimeout"] = "00:00:02" })
            .HeartbeatTimeout.ShouldBe(TimeSpan.FromSeconds(2));

    private static SignalOptions Resolve(Dictionary<string, string?> settings)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddInfrastructure()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<SignalOptions>>().Value;
    }
}
