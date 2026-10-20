using Orvano.Infrastructure.Signals;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Signals;

// Spec 0013 LSN-6: an unknown kind is logged at Warning, so the wire text it carries must not bloat or forge a line.
public sealed class SignalLogTextTests
{
    [Fact]
    [Trait("Spec", "0013:LSN-6"), Trait("Spec", "0013:AC-5")]
    public void Log_text_is_kept_when_it_is_short_and_printable() =>
        SignalListener.LogSafe("jobs").ShouldBe("jobs");

    [Fact]
    [Trait("Spec", "0013:LSN-6"), Trait("Spec", "0013:AC-5")]
    public void Log_text_replaces_control_characters_when_the_kind_holds_a_newline() =>
        SignalListener.LogSafe("a\nfake: line\r\t").ShouldBe("a?fake: line??");

    [Fact]
    [Trait("Spec", "0013:LSN-6"), Trait("Spec", "0013:AC-5")]
    public void Log_text_is_cut_to_32_characters_and_marked_when_the_kind_is_long() =>
        SignalListener.LogSafe(new string('k', 8000)).ShouldBe(new string('k', 32) + "…");
}
