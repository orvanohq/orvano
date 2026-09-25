using Npgsql;
using Orvano.Core.Data;

namespace Orvano.Server.Tests.Data;

// ORVANO_DB_URL and ORVANO_DB_ADMIN_URL accept Npgsql keywords or a postgres:// URL (spec 0002, configuration required).
public class ConnectionStringsTests
{
    [Fact]
    public void Leaves_a_keyword_connection_string_untouched()
    {
        const string keywords = "Host=db;Port=5432;Username=orvano_app;Password=secret;Database=orvano";

        Assert.Equal(keywords, ConnectionStrings.Normalize(keywords));
    }

    [Fact]
    public void Converts_a_postgres_url_into_keywords()
    {
        var csb = Parse("postgres://orvano_app:secret@db.example.com:6543/orvano");

        Assert.Equal("db.example.com", csb.Host);
        Assert.Equal(6543, csb.Port);
        Assert.Equal("orvano_app", csb.Username);
        Assert.Equal("secret", csb.Password);
        Assert.Equal("orvano", csb.Database);
    }

    [Theory]
    [InlineData("postgresql://u:p@db/orvano")]
    [InlineData("POSTGRES://u:p@db/orvano")]
    public void Accepts_the_postgresql_scheme_in_any_case(string url)
    {
        var csb = Parse(url);

        Assert.Equal("db", csb.Host);
        Assert.Equal("u", csb.Username);
    }

    [Fact]
    public void Uses_port_5432_when_the_url_has_no_port()
    {
        Assert.Equal(5432, Parse("postgres://u:p@db/orvano").Port);
    }

    [Fact]
    public void Decodes_percent_encoded_user_password_and_database()
    {
        var csb = Parse("postgres://orvano%2Badmin:p%40ss%3Aw%2Frd@db/my%20db");

        Assert.Equal("orvano+admin", csb.Username);
        Assert.Equal("p@ss:w/rd", csb.Password);
        Assert.Equal("my db", csb.Database);
    }

    [Fact]
    public void Keeps_a_colon_inside_the_password()
    {
        Assert.Equal("a:b:c", Parse("postgres://u:a:b:c@db/orvano").Password);
    }

    [Fact]
    public void Leaves_user_and_password_unset_when_the_url_has_none()
    {
        var csb = Parse("postgres://db:5433/orvano");

        Assert.Null(csb.Username);
        Assert.Null(csb.Password);
        Assert.Equal(5433, csb.Port);
    }

    [Fact]
    public void Maps_query_parameters_onto_Npgsql_settings()
    {
        var csb = Parse("postgres://u:p@db/orvano?sslmode=require&Timeout=7");

        Assert.Equal(SslMode.Require, csb.SslMode);
        Assert.Equal(7, csb.Timeout);
    }

    [Fact]
    public void Rejects_a_query_parameter_Npgsql_does_not_know()
    {
        Assert.Throws<ArgumentException>(() => ConnectionStrings.Normalize("postgres://u:p@db/orvano?not_a_setting=1"));
    }

    private static NpgsqlConnectionStringBuilder Parse(string url) => new(ConnectionStrings.Normalize(url));
}
