# 0013. Background runtime: rationale

The decision record behind [index.md](index.md). `/develop` builds from `index.md` and can skip this file.

## Context

Spec 0007 settled what Orvano's cross instance signals are (SIG-1 to SIG-3: one `LISTEN` connection per process, a small JSON payload, a heartbeat, a cache bypass while disconnected, `pg_notify` inside the change's transaction) and what cache invalidation does (CCH-1, CCH-2: memory only `HybridCache`, an `ICacheInvalidator` that removes locally and sends a `cache` signal). REP-2 named the interfaces but not their shapes. When `/develop background runtime` reached its spec gate on 2026-10-19, six things had no named source: the contract shapes, how a signal joins the caller's transaction, when the writer removes its own entry, how the bypass reaches module code, how later milestones plug in, and whether the timings are configuration.

The forces are specific. Platform writes happen through one `DbContext` per module (spec 0002 DAT-1), with `EnableRetryOnFailure` on (DAT-8), so a group of writes can be run more than once. Some writes skip `SaveChanges` entirely (`ExecuteUpdate` and `ExecuteDelete`, DAT-7; spec 0011 key rotation and token cleanup). Project creation spans three module contexts, each committing on its own (spec 0011 TXN-1). `NOTIFY` takes a database wide lock at commit, so SIG-3 limits a transaction to one signal per kind. Features 9 (LBL-3, EVL-5), 13 (the CORS cache), and 14 (labels, session revocation) all code against these contracts, so a wrong shape costs three features a rewrite.

Correctness is security relevant: labels and resource grants are cached inputs to access decisions (spec 0012). A signal that leaves before its change commits lets another instance refill the old value; a signal that is lost leaves stale access for up to the 60 second cap. The writer's own instance is the easiest place to get this wrong, because "remove locally" before the commit leaves a window where a concurrent reader caches the old row again.

## Options considered

The load bearing choice is how a signal joins the transaction (decision 2); the other five follow from it and are recorded under _Calls made while writing_.

### Option 1: Explicit context, a per context batch reset with each transaction, sent by a transaction interceptor (chosen)

`Enlist(db, kind, values)` and `Invalidate(db, tags)` take the `DbContext`, require an open transaction on it, and add to a batch held as a scoped EF service of that context. A `DbTransactionInterceptor` registered by `UseOrvanoConventions` empties the batch whenever a transaction starts, sends one `pg_notify` per kind in `TransactionCommitting`, and runs after commit actions in `TransactionCommitted`. A helper, `InTransactionAsync`, wraps the execution strategy and the transaction.

**Pros**:

- Covers `SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, and raw SQL alike, because it hooks the transaction, not the save.
- Retries reset by construction: every attempt starts a transaction, and every transaction start empties the batch.
- Several contexts in one request are unambiguous: the signal goes with the context you named.
- Batching to one signal per kind (SIG-3) falls out of the per transaction batch.
- Every platform context gets the interceptor with no module code, like `TimestampInterceptor`.

**Cons**:

- Callers must open a transaction even for a single save (one wrapper call).
- Signals must be enlisted on the context that commits; a context sharing another's transaction (`UseTransaction`) cannot signal for it.
- A transaction committed outside EF silently sends nothing.

### Option 2: `SaveChanges` interceptor, like the outbox (EVT-1)

Modules add pending signals to a scoped collector; a `SaveChangesInterceptor` writes them as part of the next `SaveChanges`.

**Pros**:

- Matches the outbox pattern modules will learn anyway.
- No explicit transaction needed for the single save case.

**Cons**:

- `ExecuteUpdate`, `ExecuteDelete`, and raw SQL never pass through `SaveChanges`, so those writes cannot signal, and spec 0011's key rotation is exactly that.
- Several saves in one transaction would each send, breaking one signal per kind, unless it also tracks the transaction, which is Option 1 with extra steps.
- A scoped collector does not know which of several module contexts the signal belongs to.
- EF may run a single command save without a transaction, so "on commit" needs care.

### Option 3: Ambient scoped batch, flushed by whichever transaction commits next

Modules call `Invalidate(tags)` with no context; a scoped batch is flushed by a `DbTransactionInterceptor` on the next commit in the request scope.

**Pros**:

- The smallest call site.
- Works with any context in the scope.

**Cons**:

- With several module contexts in one request (TXN-1), the first commit sends signals that belong to a later, maybe failing, step.
- A call with no transaction afterwards leaves the batch dangling; a background job without a request scope has no batch at all.
- Retries need explicit resets because the batch outlives the transaction.

### Option 4: Send immediately on the caller's connection

`Invalidate(db, tags)` runs `pg_notify` right away on the context's current connection and transaction.

**Pros**:

- No batch, no interceptor, and still only delivered on commit when a transaction is open.

**Cons**:

- One `NOTIFY` per call, not per kind per transaction, which SIG-3 forbids for commit contention.
- With no transaction it sends at once, before or after the change depending on call order.
- No hook for the writer's own after commit removal.

## Rationale

Option 1 is the only one that closes all four gaps the gate named without special cases. Hooking the transaction rather than the save is what makes `ExecuteUpdate` and raw SQL work, and emptying the batch at every transaction start is what makes retries, rollbacks, and several contexts (each context has its own batch and its own commit) correct with no extra code. Option 2 fails the `ExecuteUpdate` case outright, and Option 3 sends the wrong signals on the TXN-1 path, which is exactly where a failed step must not announce anything.

The cost is the explicit transaction. Refusing a call with no transaction (rather than sending at once) is deliberate: with no transaction, "send now" is right only if the caller already committed, and the code cannot tell which. An exception on day one is cheaper than a stale cache that appears only under load. `InTransactionAsync` makes the right path one call, and it also standardizes DAT-8's "transaction inside the execution strategy" rule that modules would otherwise hand write.

### Calls made while writing (recommend items)

- **Contract shapes (decision 1)**: two narrow interfaces, `ISignalBus.Enlist` and `ICacheInvalidator.Invalidate`, both synchronous and both taking the `DbContext`. Synchronous because enlisting does no I/O; sending happens at commit. No context free variant: every invalidation announces a platform write that has a transaction, and the job queue's no transaction case (JOB-4) is Infrastructure internal. Runner up: a single `ISignalBus` with a `Cache` kind and no separate invalidator, rejected because the invalidator also owns the local removal, and REP-2 and spec 0012 already name both.
- **Local removal timing (decision 3)**: on commit, before `CommitAsync` returns, plus the writer's own echo. Removing before the commit opens the refill window the gate described; removing only on the echo makes the writer's next request depend on a network round trip. Applying the echo on the writer too (no origin filtering) closes the small window between the commit and the after commit removal, at the price of a second removal. Runner up: on commit only, filtering the writer's own echo by an instance id in the payload, rejected because it leaves that window open and adds a field to SIG-1.
- **Bypass reach (decision 4)**: a `HybridCache` decorator in DI that adds `DisableLocalCacheRead | DisableLocalCacheWrite` while disconnected. Modules stay on the standard API and the bypass cannot be forgotten. Runner up: an Orvano cache wrapper modules must use, rejected because it is a second caching API to learn and enforce for no added safety; the banned symbols on direct removal (CAC-5) are the one rule modules need.
- **Hook points (decision 5)**: `ISignalHandler` per kind and `ISignalConnectionObserver` with connected, disconnected, and fallback poll callbacks, all run on one dispatch loop owned by the listener. Putting the fallback poll timer in the listener means feature 14 reads no timing options and cannot drift from SIG-2's 5 seconds. Runner up: C# events or an `IObservable` of connection state, rejected because exceptions and ordering are harder to control and each subscriber would run its own timer.
- **Timings (decision 6)**: an options class, `SignalOptions`, bound from `ORVANO_SIGNALS__*`, following JOB-4's `ORVANO_JOBS__POLLINTERVAL` precedent and spec 0002 CFG-1. Integration tests on real PostgreSQL need short timings that `FakeTimeProvider` cannot fully drive (the socket wait is real), and an options class is how this codebase does that. Runner up: constants with a test only override, rejected because a test only seam is a second configuration path that production never exercises.
- **Session overflow**: an oversize `session` batch becomes `["*"]` (reload the whole revoked set), mirroring `cache`. SIG-3 only covered `cache`; splitting into several signals would break one per kind.
- **Listener connection**: a keyed `NpgsqlDataSource` registered by Host with pooling off, because `NpgsqlDataSource.ConnectionString` drops the password and the platform pool must not lose a connection to `LISTEN` (SIG-1, PG-4). Runner up: Infrastructure binding `DatabaseOptions` itself, rejected because CFG-5 keeps those options in Host.
- **Dispatch queue**: unbounded with a Warning past 1000 items. Signals are small and rare; a bounded queue that drops would need its own lost signal recovery. Runner up: bounded with a forced resync on overflow, deferred until a measured need.
- **Log event IDs**: Infrastructure takes block 10000, the next free block (spec 0002 LOG-1).

### Reconciled with spec 0007 (2026-10-19)

Spec 0007 was updated in the same change: SIG-1 notes `["*"]` for `session`; SIG-3 and CCH-2 point here for the join and the on commit removal; REP-2 lists `SignalOptions`; _Configuration required_ lists the four `ORVANO_SIGNALS__*` variables; its feature 61 follow up points here.

### Changes from the cross check (2026-10-19)

A read only cross check on a second model (Sonnet) raised 26 items. The engineer was not in this run (it was started by another session), so the recommended fix was applied for each; each is overridable.

- **Batch storage**: was a `ConditionalWeakTable` keyed by `DbTransaction`. EF fires no rolled back event when a transaction is disposed after a throw, and Npgsql may reuse its transaction object across `Begin` calls, so a retry could inherit the failed attempt's values. Now a scoped EF service per context, emptied on transaction start and use (SND-1, SND-3), and `InTransactionAsync` rolls back explicitly (SND-6).
- **Unknown commit outcome**: a failed commit now still runs the writer's local removal, since removal is always safe (SND-3, AC-6). After commit actions never throw (SND-3). `work` must be safe to run twice (SND-6).
- **Sync commit**: refused when signals are enlisted (SND-8).
- **Race wording**: the echo narrows but cannot close the stale refill window; the bound is the expiry (CAC-4).
- **Cache setup**: 60 second default entry options, so an entry with no options cannot break the security cap; the inner cache is a keyed registration under the decorator; the flags merge and the immutability rule are named (CAC-1, CAC-2).
- **Listener details**: the bounded wait loop and heartbeat sending, an `IHeartbeatSender` test seam, a per instance application name so tests terminate one host's listener, the immediate first attempt, coalesced polls dropped on connect, observers run from a failed first connect, shutdown, and registration order (LSN-1 to LSN-6).
- **Registration**: the listener is `serve` only through `AddSignalListener`; everything else is registered for every command, with the cache bypassed when no listener runs (LSN-1, AC-12). The `"signals"` data source shares Host's connection string helper.
- **Codec edge cases**: printable ASCII values, empty lists, `"*"` collapsing the batch, the internal heartbeat encode, decode strictness, and measuring the real encoded bytes (BUS-2, SND-7).
- **Telemetry**: `sent` counts only committed signals, `hb` is not counted, `connected` is an observable gauge, and the clear reason is `connect` for every connect (LSN-7, AC-13).
- **Banned symbols**: a modules only file, so Infrastructure is not caught by it, covering both overloads (CAC-5).
- Library facts the cross check marked unsure (EF's dispose time events, Npgsql transaction reuse, `TransactionErrorEventData.Action`, HybridCache `"*"` on 10.x) are each pinned by a test in the build plan rather than assumed.
