using Orvano.Server.Install;

namespace Orvano.Server.Tests.Install;

// Spec 0006, AC-9: .env keeps every line the installer does not change.
public class EnvFileTests
{
    [Fact]
    public void Round_trips_a_file_byte_for_byte()
    {
        const string text = "# comment\n\nA=1\nexport B = two # note\nC='x y'\nD=\"q\\\"uote\"\nnot a line\n";

        Assert.Equal(text, EnvFile.Parse(text).ToString());
    }

    [Theory]
    [InlineData("A=1", "1")]
    [InlineData("A = spaced ", "spaced")]
    [InlineData("export A=1", "1")]
    [InlineData("A=x # comment", "x")]
    [InlineData("A=x#not-comment", "x#not-comment")]
    [InlineData("A='single # kept'", "single # kept")]
    [InlineData("A=\"double \\\"q\\\"\"", "double \"q\"")]
    [InlineData("A=", "")]
    public void Reads_values_as_Compose_does(string line, string expected) => Assert.Equal(expected, EnvFile.Parse(line).Get("A"));

    [Fact]
    public void The_last_assignment_wins_and_is_the_one_set_replaces()
    {
        var env = EnvFile.Parse("A=1\nB=2\nA=3\n");

        Assert.Equal("3", env.Get("A"));
        env.Set("A", "4");

        Assert.Equal("A=1\nB=2\nA=4\n", env.ToString());
    }

    [Fact]
    public void Set_appends_a_new_key_and_quotes_unsafe_values()
    {
        var env = EnvFile.Parse("A=1");

        env.Set("B", "k1:abc+/=");
        env.Set("C", "has space");
        env.Set("D", "it's mine");

        Assert.Equal("A=1\nB=k1:abc+/=\nC='has space'\nD=\"it's mine\"\n", env.ToString());
        Assert.Equal("has space", env.Get("C"));
        Assert.Equal("it's mine", env.Get("D"));
    }

    [Fact]
    public void Treats_CRLF_as_line_breaks()
    {
        var env = EnvFile.Parse("A=1\r\nB=2\r\n");

        Assert.Equal("1", env.Get("A"));
        Assert.Equal("A=1\nB=2\n", env.ToString());
    }
}
