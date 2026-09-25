using Microsoft.Extensions.Configuration;
using Orvano.Core;

namespace Orvano.Server.Tests.Core;

public class OrvanoConfigTests
{
    [Fact]
    public void Returns_a_required_value_that_is_set()
    {
        var config = Config(("ORVANO_DB_URL", "Host=db"));

        Assert.Equal("Host=db", OrvanoConfig.Required(config, "ORVANO_DB_URL"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Names_the_missing_setting_when_a_required_value_is_absent(string? value)
    {
        var config = Config(("ORVANO_DB_URL", value));

        var error = Assert.Throws<OrvanoConfigException>(() => OrvanoConfig.Required(config, "ORVANO_DB_URL"));

        Assert.Equal("ORVANO_DB_URL is not set. See spec 0002, configuration required.", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Falls_back_when_a_positive_int_is_unset(string? value)
    {
        var config = Config(("ORVANO_EVENT_RETENTION_DAYS", value));

        Assert.Equal(7, OrvanoConfig.PositiveInt(config, "ORVANO_EVENT_RETENTION_DAYS", 7));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    public void Parses_a_positive_int(string value, int expected)
    {
        var config = Config(("ORVANO_EVENT_RETENTION_DAYS", value));

        Assert.Equal(expected, OrvanoConfig.PositiveInt(config, "ORVANO_EVENT_RETENTION_DAYS", 7));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("seven")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Rejects_a_value_that_is_not_a_positive_whole_number(string value)
    {
        var config = Config(("ORVANO_EVENT_RETENTION_DAYS", value));

        var error = Assert.Throws<OrvanoConfigException>(() => OrvanoConfig.PositiveInt(config, "ORVANO_EVENT_RETENTION_DAYS", 7));

        Assert.Equal($"ORVANO_EVENT_RETENTION_DAYS must be a positive whole number, got '{value}'.", error.Message);
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
