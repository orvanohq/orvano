using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Signals;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Signals;

// Spec 0013 build plan step 1: ordering and dedupe within one batch (SND-4).
public sealed class PendingSignalsTests
{
    [Fact]
    [Trait("Spec", "0013:SND-4")]
    public void Values_are_distinct_by_ordinal_comparison_and_keep_first_enlisted_order()
    {
        var batch = new PendingSignals();

        batch.Add(SignalKind.Cache, ["b", "a", "B"]).ShouldBeTrue();
        batch.Add(SignalKind.Cache, ["a", "c", "b"]).ShouldBeFalse();

        batch.ValuesOf(SignalKind.Cache).ShouldBe(["b", "a", "B", "c"]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-4"), Trait("Spec", "0013:BUS-2")]
    public void Kinds_keep_first_enlisted_order_and_an_empty_list_adds_only_a_job()
    {
        var batch = new PendingSignals();

        batch.Add(SignalKind.Cache, []).ShouldBeFalse();
        batch.Add(SignalKind.Session, ["s"]).ShouldBeTrue();
        batch.Add(SignalKind.Job, []).ShouldBeTrue();
        batch.Add(SignalKind.Job, ["dropped"]).ShouldBeFalse();
        batch.Add(SignalKind.Cache, ["t"]).ShouldBeTrue();

        batch.Batches.Select(b => b.Kind).ShouldBe([SignalKind.Session, SignalKind.Job, SignalKind.Cache]);
        batch.ValuesOf(SignalKind.Job).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "0013:SND-3")]
    public void Clear_empties_the_values_and_the_after_commit_actions()
    {
        var batch = new PendingSignals();
        batch.Add(SignalKind.Cache, ["t"]);
        batch.AddAfterCommit(_ => ValueTask.CompletedTask);

        batch.Clear();

        batch.IsEmpty.ShouldBeTrue();
        batch.Batches.ShouldBeEmpty();
    }
}
