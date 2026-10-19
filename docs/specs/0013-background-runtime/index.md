# 0013. Background runtime: signal and cache contracts, and how a signal joins a transaction

**Date**: 2026-10-19
**Status**: In Progress

## Summary

Spec 0007 decided that Orvano's server instances tell each other about changes over PostgreSQL's `LISTEN/NOTIFY` (a built in message channel), but left open the exact code contracts and how a signal rides along with the database write it announces. This spec settles both for feature 61's first milestone (signals and cache invalidation). A module hands a signal or a cache tag to the `DbContext` (the database session object) whose transaction is open; the signal is written into that transaction just before it commits, so it goes out only if the change does, and the writer's own cache entry is removed as soon as the commit returns. Each instance runs one listener that applies incoming cache tags, falls back to reading the database whenever it might have missed a signal, and gives later features (the job runner, session revocation) a small plug in point. The job queue and outbox (milestones 2 and 3) add their own sections to this spec later.

## Requirements

**User stories**:

- As the engineer of features 9, 13, and 14, I want one small, fixed contract for "invalidate this cache tag everywhere" and "tell every instance about this", so that my module code never touches the listener or `pg_notify`.
- As a self hoster running two replicas, I want a change made through one replica to show on the other on the next request, and a dropped signal connection to cost speed, not correctness.
- As the engineer of the job runner and session revocation, I want to be told when signals may have been lost and when they flow again, so that I can catch up without my own timers for it.

**Acceptance criteria**:

- **AC-1**: `Orvano.SharedKernel` exposes `SignalKind`, `ISignalBus`, `ICacheInvalidator`, `ISignalHandler`, `ISignalConnectionObserver`, `ISignalConnectionState`, and the `AddSignalHandler<T>` and `AddSignalConnectionObserver<T>` registrations, with the signatures in _API surface_; `Orvano.Infrastructure` implements them and `Orvano.Host` registers them (spec 0007 REP-2). No module references `Orvano.Infrastructure` (existing architecture test) (BUS-1 to BUS-5).
- **AC-2**: A signal enlisted on a `DbContext` with an open transaction is sent with `pg_notify('orvano_signal', …)` on that transaction's connection just before it commits: nothing is visible to a listener before the commit, a rolled back transaction sends nothing, and a committed one sends exactly one notification per kind, carrying every value enlisted for that kind once, in first enlisted order. Rows changed by `ExecuteUpdate`, `ExecuteDelete`, or raw SQL inside the same transaction are covered the same way (SND-1 to SND-4).
- **AC-3**: Enlisting on a context with no open transaction, or on a context not set up with `UseOrvanoConventions` (so not the platform database), throws `InvalidOperationException` whose message names `InTransactionAsync` (or, for the second case, says only platform database contexts send signals); nothing is sent. Committing with a non empty batch through the synchronous `Commit` also throws, before the commit (SND-5, SND-8).
- **AC-4**: Under the execution strategy (spec 0002 DAT-8), an attempt that enlists values and then fails sends nothing; the retry that commits sends exactly one notification per kind with only its own values, also when the retry reuses the same connection. `InTransactionAsync` runs the work in a fresh transaction per attempt, rolls back explicitly on failure, and clears the change tracker before each retry (SND-3, SND-6).
- **AC-5**: Payloads are `{"v":1,"k":"<kind>","d":[…]}` and at most 999 bytes as encoded. A `cache` or `session` batch whose payload would be larger, or that holds `"*"`, is sent as `["*"]`; `job` carries `[]`. A received payload that is malformed, has another `v`, or names an unknown kind is logged at Warning and ignored without stopping the listener (SND-7, LSN-6).
- **AC-6**: On the writer's own instance, the enlisted cache tags are removed from the local `HybridCache` after the commit succeeds and before `CommitAsync` returns, so a read on that instance right after the commit runs the factory again. They are also removed when the commit fails with an unknown outcome. A rolled back transaction removes nothing (CAC-3, SND-3).
- **AC-7**: With two server hosts on one database, a cache tag invalidated in a committed transaction on host A is removed from host B's cache within 5 seconds, and the next read on host B returns the new value. This is the path spec 0012 AC-10 tests (CAC-4).
- **AC-8**: When the listener connection drops (for example `pg_terminate_backend`) or no `hb` signal arrives for `HeartbeatTimeout`, the instance counts as disconnected at once: `ISignalConnectionState.IsConnected` is false, `HybridCache` reads skip the cache (they run the factory, coalesced only with identical calls already in flight, and store nothing), each observer's `OnDisconnectedAsync` runs once, and `OnFallbackPollAsync` runs right away and then every `FallbackPollInterval` until reconnect. `/health/ready` stays healthy (LSN-3, LSN-4, CAC-2, HND-3).
- **AC-9**: The first connect attempt is immediate. While disconnected the listener retries with exponential backoff and full jitter (LSN-5), capped at `ReconnectBackoffCap`. On every successful connect, the first included, it runs `LISTEN orvano_signal`, clears the whole local cache, sets `IsConnected` to true, then runs each observer's `OnConnectedAsync`. Until the first connect succeeds the instance is disconnected, so a failed first connect also runs `OnDisconnectedAsync` once and the fallback poll from boot (LSN-2, LSN-5, HND-3).
- **AC-10**: Each received `cache`, `session`, or `job` signal reaches every `ISignalHandler` registered for its kind, in arrival order and registration order, on a dispatch loop separate from the connection read loop. A handler or observer that throws is logged at Error and does not stop other handlers or the listener (HND-1, HND-2, HND-4, LSN-6).
- **AC-11**: `SignalOptions` binds from `ORVANO_SIGNALS__HEARTBEATINTERVAL` (default 30 s), `__HEARTBEATTIMEOUT` (90 s), `__RECONNECTBACKOFFCAP` (30 s), and `__FALLBACKPOLLINTERVAL` (5 s). Each must be at least 1 second and the timeout at least twice the interval; a bad value stops startup with a message naming the variable (spec 0002 CFG-1, CFG-4) (SOP-1, SOP-2).
- **AC-12**: Modules cannot call `HybridCache.RemoveAsync` or `HybridCache.RemoveByTagAsync` (banned symbols); they invalidate only through `ICacheInvalidator`. Only `serve` runs the listener, in every role; other commands get the cache, the invalidator, and the interceptor with the listener absent, so their cache stays bypassed (CAC-5, LSN-1).
- **AC-13**: The listener's state and traffic are observable: Information logs on connect, Warning on disconnect and on each failed reconnect attempt, and the metrics `orvano.infrastructure.signals.connected` (observable gauge, 1 or 0), `orvano.infrastructure.signals.sent` and `orvano.infrastructure.signals.received` (counters tagged `kind`, `hb` not counted), and `orvano.infrastructure.cache.clears` (counter tagged `reason`: `connect` or `wildcard`) (LSN-7).

## Decision

**Chosen option**: Option 1, an explicit `DbContext` argument with a per context batch that a transaction interceptor sends just before commit; the writer removes its own entries once the commit returns; a `HybridCache` decorator gives modules the disconnect bypass without code changes.

Modules call `ICacheInvalidator.Invalidate(db, tags)` or `ISignalBus.Enlist(db, kind, values)` inside an open platform database transaction, normally through `InTransactionAsync`; the batch starts empty with every transaction, so retries, rollbacks, and several module contexts in one request each behave by construction.

**Implementation skills**: none installed for this area. The `caching` skill named in spec 0007 shows a Redis backplane; spec 0007 CCH-1 and this spec win over it (GEN-0).

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Scope and boundaries

- In: the SharedKernel contracts, the transaction join, the payload codec, the listener (connect, `LISTEN`, heartbeat, timeout, backoff, dispatch), `SignalOptions`, `HybridCache` registration and its decorator, `ICacheInvalidator`, the `cache` handler, the clear on connect, telemetry, and the tests.
- Out (later milestones of feature 61): the job queue and runner (the `job` handler and its `OnConnectedAsync` wake), schedules, and the outbox. Out (feature 14): the `session` handler, the revoked session set, and its fallback poll body. They plug into HND-1 to HND-3.
- Out: project databases. They never send signals (spec 0007 SIG-3), which SND-5 enforces.

### Rules

**Contracts (`Orvano.SharedKernel/Signals/`)**

- **BUS-1 (MUST)**: `SignalKind` is a public enum `Job`, `Cache`, `Session`, sent on the wire as `job`, `cache`, `session`. The `hb` kind is internal to the listener and is not in the enum.
- **BUS-2 (MUST)**: `ISignalBus.Enlist(DbContext context, SignalKind kind, IEnumerable<string> values)` is synchronous and does no I/O; it adds the values to the context's batch (SND-1). Values are 1 to 200 printable ASCII characters (`0x21` to `0x7E`, so no spaces or control characters), else `ArgumentException`. An empty list is a no op for `cache` and `session` and marks `job` present. Modules use it for `session` (feature 14); cache tags go through `ICacheInvalidator` and job wakes through `IJobQueue` (milestone 2).
- **BUS-3 (MUST)**: `ICacheInvalidator.Invalidate(DbContext context, IEnumerable<string> tags)` (plus a `params string[]` overload) enlists the tags as a `cache` signal and registers their local removal for after the commit (CAC-3). `"*"` means every entry. There is no variant without a context: every invalidation announces a platform database change, and that change has a transaction (SND-5).
- **BUS-4 (MUST)**: `ISignalHandler` (`SignalKind Kind { get; }`, `ValueTask HandleAsync(IReadOnlyList<string> values, CancellationToken cancellationToken)`) and `ISignalConnectionObserver` (`OnConnectedAsync`, `OnDisconnectedAsync`, `OnFallbackPollAsync`, each `ValueTask (CancellationToken)`, with default bodies that do nothing) are the only hook points (HND-1 to HND-3). `ISignalConnectionState` has `bool IsConnected { get; }`.
- **BUS-5 (MUST)**: `services.AddSignalHandler<T>()` and `services.AddSignalConnectionObserver<T>()` register singletons; they run in registration order. A handler that needs a database creates a scope through `IServiceScopeFactory`.

**Joining the transaction (SND, `Orvano.SharedKernel/Data/`)**

- **SND-1 (MUST)**: The batch (`PendingSignals`: kind to ordered distinct values, plus after commit actions) is a scoped EF internal service that `UseOrvanoConventions` registers in `OrvanoOptionsExtension.ApplyServices`, so there is exactly one per `DbContext` instance. `TransactionSignals` (public static) is the only way to reach it: `Enlist(DbContext, SignalKind, IEnumerable<string>)` returns whether this is the first enlist of that kind in the current transaction, and `AddAfterCommit(DbContext, Func<CancellationToken, ValueTask>)` adds an action. Infrastructure's `SignalBus` and `CacheInvalidator` call it; modules use the interfaces.
- **SND-2 (MUST)**: `SignalTransactionInterceptor`, a `DbTransactionInterceptor`, is registered by `UseOrvanoConventions` as a scoped EF service, the same way as `TimestampInterceptor` (spec 0011 AC-3), so every platform context has it with no module code. In `TransactionCommittingAsync` it runs one `SELECT pg_notify('orvano_signal', @payload)` per kind in the batch, on `eventData.Transaction.Connection` with the command's `Transaction` set. A failure there (including PostgreSQL error `25P02` on an aborted transaction) fails the commit, because the signal is part of the change.
- **SND-3 (MUST)**: The batch is cleared in `TransactionStartedAsync` and `TransactionUsedAsync` (so a retry starts empty even when Npgsql reuses its transaction object), and in `TransactionRolledBackAsync`. In `TransactionCommittedAsync` the after commit actions run, then the batch is cleared. In `TransactionFailedAsync` the after commit actions also run when the failed action is the commit (`TransactionErrorEventData.Action` is `"Commit"`), because the outcome is unknown and a local removal is always safe; then the batch is cleared. After commit actions catch their own exceptions and log at Error; they never fail a commit that happened. A savepoint rollback keeps enlisted values (an extra signal is harmless).
- **SND-4 (MUST)**: Within one batch, values of a kind are deduplicated with ordinal comparison and keep first enlisted order. A `job` enlist only marks the kind present.
- **SND-5 (MUST)**: `Enlist` and `Invalidate` throw `InvalidOperationException` when the context has no open transaction ("Signals join a transaction. Wrap the write in InTransactionAsync (spec 0013 SND-6).") or when `OrvanoConventions.SchemaOf` returns null for the context ("Only platform database contexts send signals (spec 0007 SIG-3)."). Enlist on the context that will commit: a context that joined another context's transaction with `UseTransaction` has its own batch, and only the committing context's batch is sent. A `DbTransaction` committed behind EF's back sends nothing.
- **SND-6 (MUST)**: `OrvanoTransactions.InTransactionAsync<T>(this DbContext db, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken, IsolationLevel isolation = IsolationLevel.ReadCommitted)` (and a non generic overload) runs `work` inside `CreateExecutionStrategy().ExecuteAsync`, in a transaction it begins and commits with `CommitAsync`, calls `RollbackAsync` when `work` or the commit throws, and calls `db.ChangeTracker.Clear()` before every attempt after the first (entities the caller loaded before the call are discarded on a retry). It throws `InvalidOperationException` when the context already has a transaction. `work` loads and changes everything it saves, and must be safe to run twice: PostgreSQL's retrying strategy cannot tell whether a commit that failed with an unknown outcome went through, so use `xmin` (DAT-9), unique keys, or idempotent statements.
- **SND-7 (MUST)**: `SignalPayload` is pure: `Encode(SignalKind, IReadOnlyList<string>)`, an internal `EncodeHeartbeat()`, and `TryDecode(string, out string kind, out IReadOnlyList<string> values)`, which returns the raw kind text so the listener handles `hb`. Encode writes `{"v":1,"k":…,"d":[…]}` with `System.Text.Json` and its default encoder, and measures the encoded UTF-8 bytes; over 999, or when the values contain `"*"`, a `cache` or `session` batch becomes `["*"]` for the wire and for the local removal. `"*"` for `session` means "reload the whole revoked set" (amends spec 0007 SIG-1). `TryDecode` ignores unknown properties and requires `v` to be 1 and `d` to be an array of non null strings; a `job` with values, or a `cache` or `session` with none, is ignored and logged at Debug.
- **SND-8 (MUST)**: The synchronous `TransactionCommitting` throws `InvalidOperationException` ("Commit with CommitAsync when signals are enlisted.") when the batch is not empty, before anything is sent. Every Orvano path commits asynchronously (spec 0002 ASY-1).

**Listener (LSN, `Orvano.Infrastructure/Signals/`)**

- **LSN-1 (MUST)**: `SignalListener` is a `BackgroundService` registered by `AddSignalListener()`, which only `ServeCommand` calls (every role, spec 0007 RUN-2). `AddInfrastructure` registers the rest (the decorator, `CacheInvalidator`, `SignalBus`, and an `ISignalConnectionState` that reports false until a listener replaces it), so `migrate`, `admin`, and tests without the listener get a bypassed cache and still enlist signals. The listener opens its connection from the keyed `NpgsqlDataSource` `"signals"` that `Orvano.Host` registers from `DatabaseOptions` through the same connection string helper as the platform data source (one helper, extracted from `PlatformDatabase`), with `Pooling=false`, `Keepalive=30`, and `ApplicationName=orvano-signals-<instance id>`, where the instance id is 8 random hex characters chosen at startup and readable by tests (`InternalsVisibleTo`).
- **LSN-2 (MUST)**: On connect it runs `LISTEN orvano_signal`, sets the last heartbeat time to now, clears the cache (CAC-6), sets connected, drops any queued fallback poll, then queues `OnConnectedAsync` for every observer. It then loops on `NpgsqlConnection.WaitAsync(TimeSpan)`, each wait bounded by the earlier of the next heartbeat due time and the timeout deadline. Notifications are handled on the read loop: `hb` updates the last heartbeat time; other kinds go to the dispatch queue.
- **LSN-3 (MUST)**: Between waits, when `HeartbeatInterval` has passed since the last send, it sends `hb` on its own connection (PostgreSQL delivers a session's own notifications to it) through an internal `IHeartbeatSender`, which tests may replace with one that sends nothing. A heartbeat from any replica counts. When `HeartbeatTimeout` passes with none, or any I/O on the connection fails, it sets disconnected at once on the read loop (so the cache bypass starts before any queued work), closes the connection, queues `OnDisconnectedAsync`, and starts the fallback poll timer and the reconnect loop.
- **LSN-4 (MUST)**: The fallback poll queues `OnFallbackPollAsync` for every observer immediately on disconnect and then every `FallbackPollInterval` until connected. At most one poll is queued at a time; a tick that finds one still queued is skipped.
- **LSN-5 (MUST)**: The first attempt (at startup, or right after a disconnect) is immediate. After failed attempt n it waits a random delay between 0 and min(`ReconnectBackoffCap`, 1 s × 2^(n − 1)) (full jitter); n resets on success. All waits and timers use the injected `TimeProvider` (spec 0002 GEN-9, ASY-7).
- **LSN-6 (MUST)**: The dispatch queue is an unbounded `Channel` read by one loop, so handlers and observers run one at a time, in arrival order, and in registration order within one item. Each call gets the host's stopping token. An exception is logged at Error with the kind and handler type and the loop continues; an `OperationCanceledException` during shutdown is not logged. A queue longer than 1000 items logs a Warning at most once per minute. Malformed or unknown payloads are logged at Warning and dropped (AC-5). On shutdown the listener stops reading, sets disconnected, drops the queue, and closes its connection.
- **LSN-7 (MUST)**: Telemetry lives in `InfrastructureTelemetry` (meter and activity source `Orvano.Infrastructure`, spec 0002 LOG-5) and `InfrastructureLog`, with a new event ID block `LogEventIds.Infrastructure = 10000`. `signals.sent` is counted by an after commit action that `SignalBus` adds once per kind per transaction (so only committed signals count), `signals.received` on the read loop, and `cache.clears` with `reason=connect` on every connect (the first included) and `reason=wildcard` on a received or committed `["*"]`. No project or user id tags.

**Hooks (HND)**

- **HND-1 (MUST)**: The `cache` handler is Infrastructure's own (CAC-4). Milestone 2 adds a `job` handler that wakes the runner; feature 14 adds a `session` handler. Several handlers per kind are allowed.
- **HND-2 (MUST)**: A handler receives the payload's values as sent, including `["*"]`, and decides what that means for its kind.
- **HND-3 (MUST)**: Observers mean: `OnConnectedAsync`, anything sent before now may have been lost, so resync (the runner wakes, the revoked set reloads in full); `OnDisconnectedAsync`, signals may be lost from now on; `OnFallbackPollAsync`, poll the database for what signals would have said (the revoked set loads changes since its last load). Timing comes from the listener (SOP-1), never from the observer.
- **HND-4 (MUST)**: Handlers and observers must be quick or hand their work off; they share one loop.

**Cache (CAC)**

- **CAC-1 (MUST)**: `AddInfrastructure` calls `AddHybridCache()` (package `Microsoft.Extensions.Caching.Hybrid` 10.x, MIT) with no `IDistributedCache` (CCH-1), and sets `HybridCacheOptions.DefaultEntryOptions` to `Expiration` and `LocalCacheExpiration` of 60 seconds, so an entry without its own options gets the security cap (CCH-2, PRF-2); key length and payload limits keep their defaults. It then moves the default `HybridCache` registration to a keyed service (`"orvano:inner"`) and registers `SignalAwareHybridCache` as the unkeyed singleton over it. The decorator overrides the abstract members (`GetOrCreateAsync<TState, T>`, `SetAsync<T>`, `RemoveAsync(string)`, `RemoveByTagAsync(string)`) and the `IEnumerable<string>` removal overloads. Modules inject `HybridCache` as usual. Cached types are immutable records (sealed, or marked `[ImmutableObject(true)]`) or serializable with `System.Text.Json`, because HybridCache copies mutable values through its serializer.
- **CAC-2 (MUST)**: While `IsConnected` is false the decorator adds `HybridCacheEntryFlags.DisableLocalCacheRead | DisableLocalCacheWrite` to every `GetOrCreateAsync` and `SetAsync`. It builds new entry options that keep the caller's expiry values and combine the flags with the caller's (or, when the caller's `Flags` is null, with `DefaultEntryOptions.Flags`); tags pass through unchanged. Removals always pass through.
- **CAC-3 (MUST)**: `CacheInvalidator.Invalidate` enlists the tags through `ISignalBus` (BUS-3) and, on the first cache enlist in the transaction, adds one after commit action that calls the inner cache's `RemoveByTagAsync` with every tag enlisted in that transaction (or `"*"`, SND-7). Nothing is removed before the commit: a reader could otherwise refill the entry from the old, still committed row.
- **CAC-4 (MUST)**: The `cache` handler calls `RemoveByTagAsync` with the received tags on every instance, the writer included. A reader that loaded the old row before the commit and stores it after both removals keeps a stale entry until it expires (at most 60 seconds for security related entries); applying the writer's echo too narrows that window but cannot close it. That bound is the accepted risk of a cache without row versions (CCH-2).
- **CAC-5 (MUST)**: A modules only banned symbols file, `server/src/BannedSymbols.Modules.txt`, added by `Directory.Build.props` for projects whose name starts with `Orvano.Modules.`, bans `HybridCache.RemoveAsync` and `HybridCache.RemoveByTagAsync` (the `string` and `IEnumerable<string>` overloads) with the reason "invalidate through ICacheInvalidator (spec 0013 CAC-5)".
- **CAC-6 (MUST)**: Clearing everything (each connect, or a received `["*"]`) is `RemoveByTagAsync("*")`, HybridCache's reserved tag for all entries. No module may use `"*"` as a real tag. A test proves the clear removes entries with and without tags on the pinned package version.

**Options (SOP)**

- **SOP-1 (MUST)**: `SignalOptions` in `Orvano.Infrastructure/Signals/` (`Section = "Signals"`): `HeartbeatInterval` 30 s, `HeartbeatTimeout` 90 s, `ReconnectBackoffCap` 30 s, `FallbackPollInterval` 5 s, each a `TimeSpan` with an XML doc comment (CFG-1).
- **SOP-2 (MUST)**: A source generated validator requires each to be at least 1 second and `HeartbeatTimeout` at least twice `HeartbeatInterval`. Tests shorten them through `OrvanoApiFactory` settings, and stop heartbeats with the `IHeartbeatSender` seam (LSN-3); operators normally leave them alone.

### Data model sketch

No tables. The state is in memory per process:

| Thing                                                                                   | Where                                                   | Lifetime                                                         |
| --------------------------------------------------------------------------------------- | ------------------------------------------------------- | ---------------------------------------------------------------- |
| Pending batch (`PendingSignals`: kind to ordered distinct values, after commit actions) | scoped EF internal service, one per `DbContext` (SND-1) | cleared at every transaction start, commit, rollback, or failure |
| Connection state (`IsConnected`, last heartbeat time, attempt count)                    | `SignalListener`, exposed as `ISignalConnectionState`   | process                                                          |
| Dispatch queue                                                                          | unbounded `Channel` in `SignalListener` (LSN-6)         | process                                                          |

### State transitions

Listener: `Disconnected` (start) → `Connecting` → `Connected` → (`hb` timeout or I/O error) → `Disconnected` → `Connecting` … → `Stopped` on host shutdown. `Connected` is entered only after `LISTEN` succeeded and the cache was cleared. The cache bypass is on in every state except `Connected`.

Pending batch: emptied at transaction start → filled by enlists → sent at `TransactionCommitting` → after commit actions run at `TransactionCommitted` (or at a failed commit) → emptied; or emptied at rollback.

### API surface

In process contracts only; no HTTP endpoint.

| Member                                                                                                   | Shape                                                                           | ACs                    | Errors                                                                        |
| -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- | ---------------------- | ----------------------------------------------------------------------------- |
| `SignalKind`                                                                                             | enum `Job`, `Cache`, `Session` (BUS-1)                                          | AC-1                   | none                                                                          |
| `ISignalBus.Enlist(DbContext, SignalKind, IEnumerable<string>)`                                          | `void`, joins the open transaction (BUS-2, SND-1)                               | AC-1, AC-2             | `InvalidOperationException` (SND-5), `ArgumentException` (bad value)          |
| `ICacheInvalidator.Invalidate(DbContext, IEnumerable<string>)`, `Invalidate(DbContext, params string[])` | `void`, enlists `cache` and the after commit removal (BUS-3, CAC-3)             | AC-2, AC-6, AC-7       | as above                                                                      |
| `ISignalHandler`                                                                                         | `Kind`, `HandleAsync(IReadOnlyList<string>, CancellationToken)` (BUS-4)         | AC-10                  | thrown exceptions are logged                                                  |
| `ISignalConnectionObserver`                                                                              | `OnConnectedAsync`, `OnDisconnectedAsync`, `OnFallbackPollAsync` (BUS-4, HND-3) | AC-8, AC-9             | thrown exceptions are logged                                                  |
| `ISignalConnectionState`                                                                                 | `bool IsConnected`                                                              | AC-8, AC-9, AC-12      | none                                                                          |
| `AddSignalHandler<T>()`, `AddSignalConnectionObserver<T>()`                                              | `IServiceCollection` extensions, singleton (BUS-5)                              | AC-1, AC-10            | none                                                                          |
| `TransactionSignals.Enlist`, `TransactionSignals.AddAfterCommit`                                         | static plumbing for Infrastructure (SND-1)                                      | AC-2, AC-6             | as `Enlist`                                                                   |
| `OrvanoTransactions.InTransactionAsync(DbContext, work, CancellationToken, IsolationLevel)`              | runs `work` in a transaction per attempt under the execution strategy (SND-6)   | AC-3, AC-4             | `InvalidOperationException` when nested; rethrows after the strategy gives up |
| `SignalPayload.Encode`, `TryDecode`                                                                      | pure codec (SND-7)                                                              | AC-5                   | `TryDecode` returns false                                                     |
| `SignalTransactionInterceptor`                                                                           | registered by `UseOrvanoConventions` (SND-2, SND-3, SND-8)                      | AC-2, AC-3, AC-4, AC-6 | a failed `pg_notify` fails the commit                                         |
| `AddInfrastructure()`, `AddSignalListener()`                                                             | registrations (LSN-1)                                                           | AC-12                  | none                                                                          |

### Value sourcing

| Action                                         | Value                                    | Source                                                                                                                 |
| ---------------------------------------------- | ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Enlist                                         | whether a transaction is open            | `context.Database.CurrentTransaction` (SND-5)                                                                          |
| Enlist                                         | the batch                                | the context's scoped `PendingSignals` (SND-1)                                                                          |
| Enlist                                         | whether the context is platform database | `OrvanoConventions.SchemaOf(options)` not null (SND-5)                                                                 |
| Send                                           | channel name                             | constant `orvano_signal` (spec 0007 SIG-1)                                                                             |
| Send                                           | payload                                  | `SignalPayload.Encode` of the batch, collapsed to `["*"]` over 999 encoded bytes or when it holds `"*"` (SND-7)        |
| Send                                           | connection and transaction               | `eventData.Transaction` and its connection (SND-2)                                                                     |
| Failed commit                                  | whether to run the removal               | `TransactionErrorEventData.Action == "Commit"` (SND-3)                                                                 |
| Local removal on commit                        | tags                                     | the batch's `cache` values for that transaction (CAC-3)                                                                |
| Cache default expiry                           | 60 s                                     | `HybridCacheOptions.DefaultEntryOptions` (CAC-1, spec 0007 CCH-2)                                                      |
| Bypass flags merge                             | caller's flags or the default flags      | entry options `Flags`, else `DefaultEntryOptions.Flags` (CAC-2)                                                        |
| Listen                                         | connection settings                      | keyed data source `"signals"` built by Host from `DatabaseOptions` through the shared helper (spec 0002 CFG-5, LSN-1)  |
| Listen                                         | application name                         | `orvano-signals-` plus the 8 hex instance id chosen at startup (LSN-1)                                                 |
| Heartbeat, timeout, backoff cap, fallback poll | durations                                | `SignalOptions` from `ORVANO_SIGNALS__*` (SOP-1)                                                                       |
| Backoff base                                   | 1 second, first attempt immediate        | constants (LSN-5)                                                                                                      |
| Bypass on or off                               | connected state                          | `SignalListener` state; false with no listener (LSN-1 to LSN-3)                                                        |
| Clear everything                               | the all entries tag                      | HybridCache reserved tag `"*"` (CAC-6)                                                                                 |
| Dispatch                                       | handlers for a kind, their order         | `ISignalHandler` registrations filtered by `Kind`, in registration order (HND-1, BUS-5)                                |
| Telemetry                                      | meter, log event IDs, counted events     | `Orvano.Infrastructure`; `LogEventIds.Infrastructure = 10000`; committed signals and received non `hb` signals (LSN-7) |
| Clock                                          | now, timers                              | injected `TimeProvider` (GEN-9)                                                                                        |

### Key invariants

- A notification is visible only after its transaction commits, and at most one per kind per transaction (SIG-3).
- A rolled back attempt sends nothing and removes nothing; every transaction starts with an empty batch.
- Whenever an instance might have missed a signal (before the first connect, while disconnected, or with no listener at all), its cache is bypassed; on every connect the whole cache is cleared before the bypass turns off.
- Payloads carry ids and tags only, never data or secrets, and stay at most 999 encoded bytes.
- Project database contexts can never enlist a signal.

### Security model

No caller facing surface. The channel is on the platform database, reachable only with the platform owner role (spec 0007 PG-2a); any session with that role could send a forged `cache` signal, which at worst empties caches (more database reads, never wrong answers), and a forged `session` signal can only revoke, never grant. Payloads hold tags and ids only. Permission answers stay uncached (CCH-2), and the 60 second default expiry (CAC-1) keeps an entry with no explicit options inside the security cap. Logs never include payload values above Debug (they hold user ids).

### Configuration required

- `ORVANO_SIGNALS__HEARTBEATINTERVAL` (30 s): how often each instance sends `hb`
- `ORVANO_SIGNALS__HEARTBEATTIMEOUT` (90 s): silence after which the instance counts as disconnected
- `ORVANO_SIGNALS__RECONNECTBACKOFFCAP` (30 s): the longest wait between reconnect attempts
- `ORVANO_SIGNALS__FALLBACKPOLLINTERVAL` (5 s): how often observers poll while disconnected

### Critical test scenarios

- Happy path: two `OrvanoApiFactory` hosts on one database; host A caches an entry tagged `t`, host B caches one tagged `t`; host A runs `InTransactionAsync` that updates a row and invalidates `t`; host A's entry is gone when `CommitAsync` returns, host B's within 5 s; verifies **AC-6**, **AC-7**.
- Join: a raw `LISTEN` connection in the test sees nothing before commit, one notification per kind after it, none after a rollback, and none for an `ExecuteUpdate` in a rolled back transaction; verifies **AC-2**.
- Refusal: `Invalidate` with no transaction and on a context without `UseOrvanoConventions` throws and sends nothing; a synchronous `Commit` with a batch throws before committing; `InTransactionAsync` inside an open transaction throws; verifies **AC-3**.
- Retry: an attempt that enlists `a` then throws a transient error, followed by a successful attempt on the same connection that enlists `b`, sends exactly one `cache` notification with `["b"]`; a `work` that throws a plain exception leaves no batch behind for the next transaction; verifies **AC-4**.
- Unknown commit: a commit forced to fail after `COMMIT` is sent (connection killed during commit) still removes the writer's local entries; verifies **AC-6**.
- Oversize: 60 tags of 36 characters produce `["*"]`, and the receiving host clears everything; a malformed payload sent by the test is ignored and the next good one applies; verifies **AC-5**, **AC-10**.
- Disconnect: the test runs `pg_terminate_backend` on host A's `orvano-signals-<id>` backend only; host A's reads run the factory every time, observers see one `OnDisconnectedAsync` and repeated `OnFallbackPollAsync`, `/health/ready` is `Healthy`, host B stays connected; after reconnect host A's cache was cleared and `OnConnectedAsync` ran; verifies **AC-8**, **AC-9**.
- First connect fails (database unreachable for the listener only): `OnDisconnectedAsync` runs once and polls start from boot; verifies **AC-9**.
- Heartbeat timeout: with the no op `IHeartbeatSender` on every host and short options, the listener counts as disconnected after `HeartbeatTimeout`; verifies **AC-8**, **AC-11**.
- A throwing handler is logged and the next signal still reaches the other handlers, in registration order; verifies **AC-10**.
- Bad options (`ORVANO_SIGNALS__HEARTBEATTIMEOUT=00:00:30` with a 30 s interval) stop startup naming the variable; verifies **AC-11**.
- Architecture: a module calling `HybridCache.RemoveByTagAsync` fails the build; `orvano migrate` starts no listener and its `ISignalConnectionState` reports false; verifies **AC-12**.
- Telemetry: the connected gauge reads 1, then 0 after the terminate; `cache.clears{reason=connect}` reads 2 after the first connect and one reconnect; `hb` is not in `signals.received`; verifies **AC-13**.

## Build plan

Tracer Bullet: steps 1 to 5 thread one cache invalidation end to end across two hosts, then steps 6 and 7 thicken it with failure handling and telemetry.

1. Contracts and codec in `Orvano.SharedKernel/Signals/`: `SignalKind`, the five interfaces, the two registration extensions, `SignalPayload` with unit tests for format, ordering, dedupe, value validation, the 999 byte and `"*"` collapse, and decode failures, satisfies **AC-1**, **AC-5**.
2. Transaction join in `Orvano.SharedKernel/Data/`: `PendingSignals` and `TransactionSignals`, `SignalTransactionInterceptor` registered by `UseOrvanoConventions` (started, used, committing, committed, rolled back, failed, and the synchronous refusal), the SND-5 refusals, and `OrvanoTransactions.InTransactionAsync`; PostgreSQL tests with a raw `LISTEN` connection for commit, rollback, `ExecuteUpdate`, the forced retry on one connection, and a throwing `work`, satisfies **AC-2**, **AC-3**, **AC-4**.
3. Listener happy path: extract the connection string helper from `PlatformDatabase` and register the keyed `"signals"` data source; `SignalOptions` with its validator; `SignalListener` connects, listens, decodes, and dispatches to handlers on its own loop; `AddInfrastructure` registers the shared pieces and `AddSignalListener` (called by `ServeCommand` only) the listener, satisfies **AC-10**, **AC-11**, **AC-12**.
4. Cache: `AddHybridCache` with the 60 second defaults, the keyed inner registration and `SignalAwareHybridCache`, `CacheInvalidator` with the after commit removal (also on a failed commit), the `cache` handler, the `"*"` clear test, and `BannedSymbols.Modules.txt`, satisfies **AC-6**, **AC-12**.
5. Two host integration test in `Orvano.Host.IntegrationTests` (the happy path scenario), which also proves the path spec 0012 build plan step 8 needs, satisfies **AC-7**.
6. Failure handling: the bounded wait loop, heartbeat send through `IHeartbeatSender` and the timeout, disconnect on I/O error, bypass, observers (`OnDisconnectedAsync`, the coalesced `OnFallbackPollAsync` timer, `OnConnectedAsync` after the clear), the immediate first attempt and full jitter backoff, shutdown, and their tests (terminate one host's backend, failed first connect, silent heartbeat, throwing handler, unknown commit), satisfies **AC-6**, **AC-8**, **AC-9**, **AC-10**.
7. Telemetry and logs: `InfrastructureTelemetry`, `InfrastructureLog`, `LogEventIds.Infrastructure = 10000`, the committed only `sent` count, and the gauge and counter test, satisfies **AC-13**.

## Consequences

**Positive**:

- Module code for "invalidate everywhere" is one line inside a transaction, and the signal cannot leave before the change or survive a rollback.
- Retries, rollbacks, `ExecuteUpdate`, and several module contexts in one request (spec 0011 TXN-1: each step commits its own context, so each step's signals ride its own commit) need no special handling.
- The writer sees its own change on its next request, and every other instance within milliseconds; a lost signal, or a reader racing the commit, costs at most the entry's expiry (60 seconds for security related entries).
- Modules keep using plain `HybridCache`; the bypass is invisible to them, and an entry with no options still gets the security cap.
- The job runner and feature 14 plug in through two small interfaces and get timing, backoff, and resync for free.

**Negative / tradeoffs**:

- Every write that invalidates a cache must run in an explicit transaction (`InTransactionAsync`), even a single `SaveChanges`. That is one wrapper call per write path, enforced by an exception, not the compiler.
- `work` inside `InTransactionAsync` must be safe to run twice, because a commit with an unknown outcome is retried. That is DAT-8's existing risk, now stated.
- Signals must be enlisted on the context that commits; a context sharing another's transaction cannot signal for it.
- While disconnected, and in commands without a listener, every cached read hits the database; a long outage of the signal connection (but not of the database) raises platform database load.
- The writer applies its own tags twice (on commit and on the echo). Cheap, but visible in metrics.
- Handlers share one loop; a slow one delays the rest (HND-4).

**Neutral**:

- Spec 0007 gains a `["*"]` meaning for `session`, `SignalOptions` in REP-2's list, and four `ORVANO_SIGNALS__*` variables.
- Feature 14's label writes (spec 0012 LBL-3) and every EVL-5 grant change call `Invalidate` inside `InTransactionAsync`.
- A new package, `Microsoft.Extensions.Caching.Hybrid` (MIT, on spec 0009 DEP-1's list), and a new modules only banned symbols file.

## Follow-up

- [ ] Milestone 2 (job queue, runner, schedules): extend this spec with the `IJobQueue` and `IJobHandler<T>` contracts, the `job` handler, and how enqueue without a transaction sends its signal (spec 0007 JOB-4), before `/develop` builds it.
- [ ] Milestone 3 (outbox): extend this spec with `IOutbox`, `IOutboxRegistry`, and the dispatcher wake (spec 0007 EVT-1 to EVT-3).
- [ ] Feature 9: run spec 0012 build plan step 8 (AC-10) once step 5 here lands.
- [ ] Feature 14: implement the `session` handler and an `ISignalConnectionObserver` for the revoked set (HND-1, HND-3), and wrap label writes in `InTransactionAsync` (LBL-3).
- [ ] Feature 13: invalidate `orvano:platform:cors:<project id>` through `ICacheInvalidator` (spec 0007 COR-1).
- [ ] `/sync`: add the `ORVANO_SIGNALS__*` variables to `server/AGENTS.md` and the install guide when they are built.
