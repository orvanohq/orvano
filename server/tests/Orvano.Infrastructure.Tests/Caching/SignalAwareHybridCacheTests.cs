using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Orvano.Infrastructure.Caching;
using Orvano.SharedKernel.Signals;

using Shouldly;

using Xunit;

namespace Orvano.Infrastructure.Tests.Caching;

// Spec 0013 build plan step 4: the cache registration, its decorator, and the clear (CAC-1, CAC-2, CAC-6).
public sealed class SignalAwareHybridCacheTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "0013:CAC-1")]
    public void Modules_get_the_decorator_and_entries_without_options_expire_after_60_seconds()
    {
        using ServiceProvider services = Build(connected: true);

        services.GetRequiredService<HybridCache>().ShouldBeOfType<SignalAwareHybridCache>();
        services.GetRequiredKeyedService<HybridCache>(SignalAwareHybridCache.InnerKey).ShouldNotBeOfType<SignalAwareHybridCache>();
        HybridCacheEntryOptions defaults = services.GetRequiredService<IOptions<HybridCacheOptions>>().Value.DefaultEntryOptions!;
        defaults.Expiration.ShouldBe(TimeSpan.FromSeconds(60));
        defaults.LocalCacheExpiration.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    [Trait("Spec", "0013:CAC-2"), Trait("Spec", "0013:AC-8")]
    public async Task Reads_run_the_factory_every_time_and_store_nothing_when_disconnected()
    {
        using ServiceProvider services = Build(connected: false);
        HybridCache cache = services.GetRequiredService<HybridCache>();
        int calls = 0;

        await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(++calls), cancellationToken: Token);
        await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(++calls), cancellationToken: Token);
        await cache.SetAsync("s", 1, cancellationToken: Token);

        calls.ShouldBe(2);
        int stored = await services.GetRequiredKeyedService<HybridCache>(SignalAwareHybridCache.InnerKey)
            .GetOrCreateAsync("s", _ => ValueTask.FromResult(-1), cancellationToken: Token);
        stored.ShouldBe(-1);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-2")]
    public async Task Reads_are_cached_when_connected()
    {
        using ServiceProvider services = Build(connected: true);
        HybridCache cache = services.GetRequiredService<HybridCache>();
        int calls = 0;

        await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(++calls), cancellationToken: Token);
        int second = await cache.GetOrCreateAsync("k", _ => ValueTask.FromResult(++calls), cancellationToken: Token);

        second.ShouldBe(1);
        calls.ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-2")]
    public async Task Bypass_keeps_the_callers_expiry_and_tags_and_adds_to_the_callers_flags()
    {
        var inner = new RecordingCache();
        var cache = new SignalAwareHybridCache(inner, new FixedState(false), Options.Create(new HybridCacheOptions()));
        var options = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(2),
            LocalCacheExpiration = TimeSpan.FromMinutes(1),
            Flags = HybridCacheEntryFlags.DisableCompression,
        };

        await cache.GetOrCreateAsync("k", 0, (_, _) => ValueTask.FromResult(1), options, ["t"], Token);

        inner.Options.ShouldNotBeNull();
        inner.Options.Expiration.ShouldBe(TimeSpan.FromMinutes(2));
        inner.Options.LocalCacheExpiration.ShouldBe(TimeSpan.FromMinutes(1));
        inner.Options.Flags.ShouldBe(HybridCacheEntryFlags.DisableCompression
            | HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite);
        inner.Tags.ShouldBe(["t"]);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-2")]
    public async Task Bypass_adds_to_the_default_flags_when_the_caller_sets_no_options()
    {
        var inner = new RecordingCache();
        var defaults = new HybridCacheOptions { DefaultEntryOptions = new() { Flags = HybridCacheEntryFlags.DisableDistributedCache } };
        var cache = new SignalAwareHybridCache(inner, new FixedState(false), Options.Create(defaults));

        await cache.SetAsync("k", 1, cancellationToken: Token);

        inner.Options!.Flags.ShouldBe(HybridCacheEntryFlags.DisableDistributedCache
            | HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-2")]
    public async Task Options_pass_through_unchanged_when_connected()
    {
        var inner = new RecordingCache();
        var cache = new SignalAwareHybridCache(inner, new FixedState(true), Options.Create(new HybridCacheOptions()));
        var options = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(2) };

        await cache.SetAsync("k", 1, options, cancellationToken: Token);

        inner.Options.ShouldBeSameAs(options);
    }

    [Fact]
    [Trait("Spec", "0013:CAC-6")]
    public async Task Removing_the_star_tag_clears_entries_with_and_without_tags_on_the_pinned_package()
    {
        using ServiceProvider services = Build(connected: true);
        HybridCache cache = services.GetRequiredKeyedService<HybridCache>(SignalAwareHybridCache.InnerKey);
        await cache.SetAsync("untagged", 1, cancellationToken: Token);
        await cache.SetAsync("tagged", 1, tags: ["t"], cancellationToken: Token);

        await cache.RemoveByTagAsync("*", Token);

        (await cache.GetOrCreateAsync("untagged", _ => ValueTask.FromResult(2), cancellationToken: Token)).ShouldBe(2);
        (await cache.GetOrCreateAsync("tagged", _ => ValueTask.FromResult(2), cancellationToken: Token)).ShouldBe(2);
    }

    [Fact]
    [Trait("Spec", "0013:LSN-1"), Trait("Spec", "0013:AC-12")]
    public void A_process_without_the_listener_is_never_connected_and_runs_no_listener()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddLogging()
            .AddInfrastructure()
            .BuildServiceProvider();

        services.GetRequiredService<ISignalConnectionState>().IsConnected.ShouldBeFalse();
        services.GetServices<IHostedService>().ShouldBeEmpty();
        services.GetRequiredService<ISignalBus>().ShouldNotBeNull();
        services.GetRequiredService<ICacheInvalidator>().ShouldNotBeNull();
    }

    [Fact]
    [Trait("Spec", "0013:LSN-1")]
    public void The_listener_replaces_the_disconnected_state_when_added()
    {
        IServiceCollection services = new ServiceCollection().AddInfrastructure().AddSignalListener();

        services.Count(d => d.ServiceType == typeof(ISignalConnectionState)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IHostedService)).ShouldBe(1);
    }

    internal static ServiceProvider Build(bool connected) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
        .AddLogging()
        .AddInfrastructure()
        .Replace(ServiceDescriptor.Singleton<ISignalConnectionState>(new FixedState(connected)))
        .BuildServiceProvider();

    internal sealed class FixedState(bool connected) : ISignalConnectionState
    {
        public bool IsConnected => connected;
    }

    private sealed class RecordingCache : HybridCache
    {
        public HybridCacheEntryOptions? Options { get; private set; }

        public IEnumerable<string>? Tags { get; private set; }

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            Options = options;
            Tags = tags;
            return factory(state, cancellationToken);
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            Tags = tags;
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
