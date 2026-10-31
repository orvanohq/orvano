using Orvano.Modules.Auth.Contracts;
using Orvano.Modules.Auth.Passwords;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.Modules.Auth.Tests.Passwords;

public sealed class PasswordWorkTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact, Trait("Spec", "0017:PSW-5"), Trait("Spec", "0006:PWD-5"), Trait("Spec", "0017:AC-5")]
    public async Task VerifyAsync_runs_one_verify_and_returns_false_when_the_hash_is_null()
    {
        var hasher = new RecordingHasher();
        using var work = new PasswordWork(hasher, 2, Argon2Cost.Default);

        Result<bool> result = await work.VerifyAsync("any password", null, Token);

        result.Value.ShouldBeFalse();
        hasher.Verified.ShouldHaveSingleItem().ShouldContain("m=65536,t=3,p=1");
    }

    [Fact, Trait("Spec", "0017:PSW-5")]
    public async Task VerifyAsync_returns_false_even_when_the_hasher_accepts_the_dummy()
    {
        using var work = new PasswordWork(new RecordingHasher { Answer = true }, 2, Argon2Cost.Default);

        (await work.VerifyAsync("any password", null, Token)).Value.ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0017:PSW-5")]
    public async Task VerifyAsync_uses_the_real_hasher_and_the_dummy_never_matches_when_hashes_are_real()
    {
        using var work = new PasswordWork(new Argon2idPasswordHasher(), 2, Argon2Cost.Default);
        string hash = (await work.HashAsync("correct horse", Token)).Value!;

        (await work.VerifyAsync("correct horse", hash, Token)).Value.ShouldBeTrue();
        (await work.VerifyAsync("wrong horse", hash, Token)).Value.ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0017:PSW-4"), Trait("Spec", "0006:PWD-2"), Trait("Spec", "0017:AC-5")]
    public void NeedsRehash_is_true_only_when_the_stored_parameters_are_weaker()
    {
        using var work = new PasswordWork(new Argon2idPasswordHasher(), 2, Argon2Cost.Default);
        string current = new Argon2idPasswordHasher(Argon2Cost.Default).Hash("a password");
        string weak = new Argon2idPasswordHasher(Argon2Cost.Floor).Hash("a password");
        string stronger = new Argon2idPasswordHasher(new Argon2Cost(128 * 1024, 4)).Hash("a password");

        work.NeedsRehash(weak).ShouldBeTrue();
        work.NeedsRehash(current).ShouldBeFalse();
        work.NeedsRehash(stronger).ShouldBeFalse();
        work.NeedsRehash("not a hash").ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0017:PSW-4"), Trait("Spec", "0017:AC-4")]
    public async Task HashAsync_answers_503_with_retry_after_5_when_every_slot_stays_busy()
    {
        var gate = new ManualResetEventSlim();
        using (gate)
        using (var work = new PasswordWork(new BlockedHasher(gate), 1, Argon2Cost.Default))
        {
            Task<Result<string>> holder = work.HashAsync("first", Token);
            var waited = System.Diagnostics.Stopwatch.StartNew();

            Result<string> refused = await work.HashAsync("second", Token);

            waited.Stop();
            gate.Set();
            await holder;
            refused.Error!.Code.ShouldBe(ErrorCodes.Unavailable);
            refused.Error.RetryAfter.ShouldBe(TimeSpan.FromSeconds(5));
            waited.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(4.5));
        }
    }

    private sealed class RecordingHasher : IPasswordHasher
    {
        public List<string> Verified { get; } = [];

        public bool Answer { get; init; }

        public string Hash(string password) => "hash";

        public bool Verify(string password, string hash)
        {
            Verified.Add(hash);
            return Answer;
        }
    }

    private sealed class BlockedHasher(ManualResetEventSlim gate) : IPasswordHasher
    {
        public string Hash(string password)
        {
            gate.Wait(TimeSpan.FromSeconds(30));
            return "hash";
        }

        public bool Verify(string password, string hash) => false;
    }
}
