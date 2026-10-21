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

### Review settlement (2026-10-19)

The fresh model review ([report](../../reviews/2026-10-19-review-background-runtime.md)) left six items the fix commit `1a521f6` could not close without changing a rule. This run settled them. Like the cross check above, it was started by another session with the engineer away, so each pick is the recommendation and each is overridable.

- **Minor 2, `signals.sent` on an unknown commit (SND-1, SND-3, LSN-7, AC-13).** Options: (a) a SharedKernel hook that runs only on a confirmed commit, (b) reword LSN-7 so the counter includes unknown outcomes, (c) tag `sent` with an `outcome`. Picked (a), `TransactionSignals.AddOnCommitted`. The counter exists to compare sent with received during incidents, which is exactly when unknown outcomes happen, so an overcount defeats it. (c) also needs the action to know the outcome, so it costs the same API change and adds a tag nobody asked for. The API change is cheap now: `TransactionSignals` is Infrastructure only and banned in modules, and backwards compatibility is not required before beta.
- **Minor 3, enlisting on a joined context (SND-5, AC-3).** Options: (a) throw on a context whose transaction was joined with `UseTransaction`, (b) keep the documented rule, (c) send the joined batch through the owner. Picked (a). The fix session's objection, that a joined context can be the one that commits, is true in EF but not a pattern Orvano uses: spec 0011 TXN-1 has each step begin and commit its own context through `InTransactionAsync`, and no code calls `UseTransaction`. A loud exception beats a silent stale window of up to 60 seconds. (c) would need a registry across contexts for a pattern nobody uses. The residual case (committing through a joined context loses the owner's batch) is named as unsupported.
- **Minor 7, reconnect floor (LSN-5, AC-9).** Options: (a) reset the attempt count only after a stable period, (b) a fixed minimum delay after a short connection, (c) keep LSN-5 as is. Picked (a) with the stable period set to `HeartbeatInterval`, so a flapping connection backs off on the same full jitter curve up to `ReconnectBackoffCap`, and a drop after a long lived connection still reconnects at once. No new option: `HeartbeatInterval` is the earliest a dead connection can be noticed anyway. (c) lets a flap clear the whole cache in a tight loop, which turns caching off for everything.
- **Minor 10 rest, module isolation (CAC-1, CAC-5, AC-12).** The `"orvano:inner"` string becomes an internal object key, so modules cannot name it (cheaper and stronger than a ban, since keyed lookups by string cannot be banned as symbols). `NpgsqlDataSource` open and create members are banned for modules: spec 0007 PG-3 already routes project connections through `IProjectDbConnectionFactory` and platform writes go through a `DbContext`, so a module has no legitimate use, and a raw connection is the one way to write without joining the signal transaction. `SignalPayload` and `NpgsqlConnection.Wait` are banned with the listener types. Raw SQL text cannot be caught by symbol bans, so a source scan test looks for `pg_notify` and `orvano_signal` in module files (the plain word `LISTEN` was rejected as too likely in comments).
- **Nit 2, announce disconnected at boot (LSN-2, AC-9).** Picked yes. The instance is disconnected from boot, and HND-3 says observer timing comes from the listener. Without it, feature 14's revoked set would stay unloaded for the whole first connect attempt (up to the 15 second connect timeout), which is a security window, not just a slow start. The cost is one fallback poll per observer per boot.
- **Dispatch limit (LSN-6, LSN-8, SOP-1, SOP-2, AC-8, AC-10, AC-11).** Options for the per call bound: (a) keep `HeartbeatTimeout`, (b) a new `HandlerTimeout` option, (c) a constant. Picked (b) at 10 seconds, at most `HeartbeatTimeout`: 90 seconds is far too long to hold every cache signal, and operators with a slow platform database may need to raise it, which a constant would not allow. For "no progress counts as disconnected", picked yes (LSN-8): the per call bound alone still lets a queue of slow calls delay cache signals without limit while `IsConnected` stays true, which breaks the promise that a lost or late signal costs speed, not correctness. The threshold reuses `HeartbeatTimeout`, the staleness a silent connection already gets; items from before the latest connect are ignored because the connect clear already covers them, which keeps a stuck old call from making the listener flap.

Also recorded so the spec matches the code in `1a521f6`: `ISignalBus.Enlist` refuses `Cache` (BUS-2), `TryDecode` applies BUS-2's value rule on receive (SND-7), the signals data source sets `Timeout` and `CommandTimeout` explicitly (LSN-1), throwing after commit actions are caught inside `PendingSignals` (SND-3), and the dispatch loop catches per item (LSN-6).

## Milestone 2: job queue (2026-10-19)

### Context

Spec 0007 JOB-1 to JOB-6 settled the queue's behavior: a `jobs` schema, at least once delivery, `SKIP LOCKED` claims, leases, backoff with jitter, a `job` signal per SIG-3, a 5 second poll, a concurrency cap at half the platform pool, and schedules that fire once through a dedupe key. REP-2 named `IJobQueue` and `IJobHandler<T>` but not their shapes. `/develop` would have had to invent the contract, the columns' details, how the row joins the caller's transaction, the lease and renewal mechanics, dead job handling, how wake and poll combine, how schedules are declared, the metrics, and how tests control time. Features 13, 14, 15, and 29 all code against this, and the outbox (milestone 3) enqueues through it.

The forces: the job row lives in Infrastructure's `jobs` schema, while the caller's change lives in a module's own `DbContext` and schema (spec 0002 DAT-1, spec 0011 MOD-3), and the two must commit together. EF's execution strategy can run the caller's work twice (DAT-8, SND-6). The signal machinery already gives one transaction join per context (SND-1 to SND-8). `IProjectContext` has exactly one setter today (spec 0012 PRJ-3). This is the repo's first `DbContext` and first migration. And spec 0002 GEN-9 forbids reading the clock anywhere but `TimeProvider`.

### Options considered (how a job joins the caller's transaction)

#### Option J1: SQL on the caller's context, signal enlisted on the same context (chosen)

`EnqueueAsync(context, job, …)` runs `INSERT INTO jobs.job … ON CONFLICT DO NOTHING` through `context.Database.ExecuteSqlAsync`, on the caller's connection and transaction, and enlists `job` on that context.

- Pros: one transaction by construction, the same one the signal joins; retries and rollbacks need nothing new (SND-3, SND-6); works on any platform context, so the outbox dispatcher (EVT-3) and every module use one path; the module never sees `jobs` tables.
- Cons: Infrastructure SQL runs on a module's context (a write into another schema, by Infrastructure code, not the module's); the insert happens at call time, not at `SaveChanges`; the column list lives in SQL as well as in the EF model, so a test must keep them in step (AC-15, AC-16).

#### Option J2: `JobsDbContext` sharing the caller's connection with `UseTransaction`

Build a `JobsDbContext` on the caller's `DbConnection` and join its `DbTransaction`, then `Add` and `SaveChanges`.

- Pros: typed EF insert, change tracking.
- Cons: SND-5 refuses signals on a joined context, so the signal would still need the caller's context; a second context per enqueue on a shared connection is easy to get wrong under the execution strategy; spec 0013 already names `UseTransaction` as unsupported.

#### Option J3: Enqueue through an outbox like row in the caller's schema

Each module writes a pending job row in its own schema, and a mover copies it into `jobs.job`.

- Pros: modules write only their own schema.
- Cons: a second queue per module, a mover loop, and seconds of delay for every job; it is the outbox's shape (milestone 3) applied to the queue itself, doubling the machinery.

#### Option J4: Enqueue after commit

The caller commits, then enqueues in its own transaction.

- Pros: simplest code.
- Cons: a crash between the two loses the job, which JOB-4 exists to prevent.

### Rationale

J1 is the only option that makes "the job exists if and only if the change committed" true with no new machinery, because it reuses the exact transaction the signal already rides. The schema boundary MOD-3 protects is about module code reading other modules' tables; here the code is Infrastructure's, and the module only calls `IJobQueue`. J2 fights SND-5, J3 builds a second outbox, and J4 breaks JOB-4.

### Calls made while writing (recommend items, milestone 2)

The engineer was not in this run (another session started it), so each pick is the recommendation and is overridable.

- **Job identity**: a record implementing `IJob` with a `static abstract string Kind`, so enqueue needs no lookup to name the kind and the name sits beside the payload type. Runner up: a `[JobKind]` attribute, rejected because it needs reflection and fails at runtime instead of compile time.
- **Registration**: `AddJobHandler<TJob, THandler>(JsonTypeInfo<TJob>, configure)`, with lease, max attempts, and timeout per kind. Taking the `JsonTypeInfo` follows JSN-1's source generation and lets Infrastructure call handlers through a delegate, with no reflection. Runner up: per enqueue options for attempts and lease, rejected because the same kind should behave the same way everywhere.
- **No transaction**: a separate context free overload that runs its own transaction on `JobsDbContext`, rather than sending at once on the caller's context. The overload with a context still refuses a context without a transaction (SND-5), so a caller who has a transaction cannot accidentally enqueue outside it.
- **Lease and fencing**: the claim's `attempt` plus `locked_by` fence every later write, so a worker whose lease expired cannot overwrite the new holder's outcome. Renewal every third of the lease is JOB-2's; a lost renewal cancels the handler. Runner up: `xmin` on the row, rejected because the claim itself changes `xmin` and the fence must survive renewals.
- **Unknown kinds and bad payloads**: claimed and treated as a failed attempt with a clear `last_error`, rather than left queued forever or filtered out of claims. First written as "dead at once"; the cross check below changed it so a rollback to an older version drains them.
- **Backoff**: equal jitter (half the curve plus a random half), not full jitter as for the listener (LSN-5). A retry of a failed job should never come back at once; the listener's reconnect should.
- **Timeout**: a per kind `Timeout` (default 1 hour) on top of the lease, so a handler that hangs while renewing cannot hold a slot forever; at the limit the attempt fails and the task is abandoned, like LSN-6.
- **Shutdown**: a cancelled handler is released with its attempt restored, so deploys do not burn attempts; a hard kill falls back to the lease.
- **Wake and poll**: one coalescing wake channel fed by the `job` signal, an always on poll, the connect observer, and a full claim; no separate fallback poll while disconnected, since the poll is already the fallback and is the only path for future jobs.
- **Schedules in code**: declared with `AddJobSchedule` and synced at worker start, the table keeping `cron` (for change detection), `enabled`, `next_run_at`, and `last_fired_at`, with kind and payload read from the registration by name. The first schedules are all Orvano's own maintenance, which belongs in code review, not in data. Runner up: schedules only in the table, seeded by migrations, rejected because a cron change would need a migration and drift is invisible.
- **Missed fires**: collapse to one fire, because every planned schedule is cleanup or rotation, where running ten times after an outage is waste.
- **Project scope**: `JobAccessScope`, a public SharedKernel type banned in modules, rather than `InternalsVisibleTo` from SharedKernel to Infrastructure, which would expose every SharedKernel internal. It amends spec 0012 PRJ-3 with exactly one more setter.
- **Concurrency default**: also capped at half the pool. JOB-4's default (up to 16) breaks its own rule at the default pool of 30 on any machine with 8 or more cores; failing startup with default settings is wrong, so the default yields and only an explicit value fails.
- **Clock**: the app's `TimeProvider` for every time, passed as SQL parameters, rather than PostgreSQL's `now()`. GEN-9 requires it, and it is what lets tests drive leases, backoff, and cron with `ManualClock`; the cost is trusting replica clocks (CLM-8).
- **`trace_parent` column**: added to JOB-1's list so an attempt's trace links to the request that queued it; cheap, and the alternative (no link) makes a failing job hard to trace back.
- **`CancelProjectJobsAsync`**: added now because spec 0011 LIF-2 step 1 needs it and modules cannot touch `jobs.job`; leaving it to feature 13 would reopen this contract.
- **Log event IDs**: jobs take `LogEventIds.Infrastructure + 100` to `+ 199`, leaving the first hundred to signals.

### Reconciled with specs 0007 and 0012 (2026-10-19)

Spec 0007 was updated in the same change: JOB-1 adds `trace_parent` and notes the dedupe index is a plain unique index; JOB-2 points to CLM for the claim and fence; JOB-4 caps the default concurrency at half the pool and points to ENQ for the transaction join and the context free overload; JOB-5 says schedules are declared in code and synced (SCH-2); _Configuration required_ shows the new default. Spec 0012 PRJ-3 allows `JobAccessScope` as the one other setter.

### Cross check of milestone 2 (2026-10-19)

A read only cross check on another model (its report is kept outside the repo, in the session notes as `feature-61-job-queue-crosscheck.md`) found six gaps to settle before `/develop` and a set of smaller ones. The engineer approved applying its recommended fixes. Each change, by the report's IDs:

- **G1, timed out handler (S1, V4)**: the slot stays taken, and the job id stays in an in process running set, until the handler returns; the outcome is still written at once, and the claim skips ids in that set (CLM-2, OUT-2, AC-19, AC-21). Lease loss already worked this way.
- **G2, claim index (S2)**: `ix_job_due` covers `queued` rows only, a new `ix_job_lease` covers `running` rows by `locked_until`, and the claim takes due rows first, then expired leases, as two `SKIP LOCKED` subselects under one `LIMIT` (JDB-2, CLM-1, spec 0007 JOB-1). The claim SQL is now written out (V15). HOT updates are no longer promised.
- **G3, renewal (S3, V3)**: one renewal in flight per job, a single attempt with no execution strategy retry, a command timeout of lease ÷ 3, stopped and awaited before the outcome write, and a no row result after the handler returned is ignored; the minimum lease rises to 1 minute (CLM-5, CLM-8, JQ-5, AC-20).
- **G4, test driver (V12, V13)**: `JobsDriver.RunDueAsync` syncs schedules before the tick, so it works on `api` test hosts, and stops after 1000 attempts per call (JTS-2, AC-29).
- **G5, cancellation (V2, V16, S4)**: a new OUT-8 fixes the cause order (lease lost, timed out, shutdown, else the handler's own) and runs outcome and release writes on a 10 second clock bound source, never the stopping token; OUT-6 stops the wake loop and awaits a claim or tick in flight before cancelling handlers, and a handler finishing during shutdown records its outcome (AC-21).
- **G6, startup errors (V10)**: the registration extensions throw `ConfigurationException` (already caught in `ServeCommand.Build`), and `StartupChecks` turns `JobRegistry` errors into `StartupFailedException` (already caught in `RunAsync`), with `AddJobRunner` registered after it, so no bad registration ends in a stack trace (JQ-5, SCH-1, AC-24). This needs no change to `ServeCommand`'s catch blocks.
- **Smaller gaps**: `/develop` picks and records the `Cronos` version (V1); the full claim wakes on any job finishing while the last claim was full (V5); a 25 second host shutdown timeout and `stop_grace_period: 35s` (V6); histogram buckets (V7); `jobs.cleanup` with `MaxAttempts` 3 and an `InfrastructureJsonContext` (V8); schedule payloads serialized and size checked by `JobRegistry` with the kind's type info (V9, S5 poison row); `RequiresProject` per kind (1b item 10, V11); the `::jsonb` cast (V14); backoff in `double` with a clamped exponent and a test at attempt 100; schedule tick errors isolated from the claim and syncs in name order (S5); a cron with no next occurrence rejected (S6); poll granularity of schedules documented; `CancelProjectJobsAsync`'s races stated in AC-30 for feature 13 (S7, V17 latency stated); unknown kinds and unreadable payloads retry instead of dying at once (S8); a one connection per handler rule (S9); the context free overload's limit documented (1b item 9); the instance id must be new every start; the NOTIFY commit lock, non HOT updates, and `MaxAttempts` 1 noted in Consequences and JQ-5 (S10); a per kind concurrency limit as a feature 23 follow up.
- **`jobs.schedule` without `kind` and `payload` (S5, simpler option)**: taken. The registration is the only source, which removes a payload comparison and a second copy that could drift; the cost is that the table alone does not say what a schedule runs.

Left open for the engineer, because the report gave no clear pick (listed in index.md _Follow-up_): capping the renewal interval at 1 minute (V17), lowering the default concurrency to a third of the pool (S9), queue depth and dead count gauges (S10), one renewal statement per worker, a `lease_token` fence column, and storing only the exception type in `last_error`.

### Review settlement of milestone 2 (2026-10-20)

The fresh model review of the built job queue ([report](../../reviews/2026-10-20-review-background-runtime-job-queue.md)) left three items that needed a rule change, and the verify report ([report](../../reviews/2026-10-19-verify-background-runtime-job-queue.md)) owed two corrections. Each was decided on recommendation; the engineer approves or overrides.

- **Dedupe scope (review Minor 5)**: the unique index moves from `dedupe_key` alone to `(kind, project_id, dedupe_key)`, `NULLS NOT DISTINCT`, partial on `dedupe_key IS NOT NULL` (JDB-2, ENQ-1). The project matters as much as the kind: a key built from one project's input (a client supplied message id, for example) must never suppress another project's job, and Orvano treats tenant isolation as a default. `NULLS NOT DISTINCT` (PostgreSQL 15 and later; the platform database is PostgreSQL 18) keeps schedules, which have no project, colliding with themselves; the predicate keeps keyless rows out. Options weighed: per kind only (closes the module trap but not the tenant one); prefixing the stored key with the kind in code (works, but the column then holds a different value than the caller passed, and the project would need the same trick); keeping it global with a doc comment (what #56 did, which leaves the trap). The cost is a wider index and a second migration; backwards compatibility is not required before beta.
- **Pool rule (review Minor 6)**: count the runner's one loop connection, not the renewals and not the listener (JOP-2, JOP-4). The limit becomes half the pool less one. That one change makes both bounds hold: steady state jobs use at most half the pool, and the worst case (every handler and every renewal at once) uses at most the pool less one, so requests are never locked out. Options weighed: documenting half as typical only (what #56 did; leaves the pool exhaustion case real); reserving room for every renewal, C ≤ (pool / 2 − 1) / 2 (7 at the default pool, halving throughput for a millisecond overlap); a separate pool for the runner's own statements (adds up to Concurrency + 1 connections per replica to PG-4's `max_connections` sum). The listener is excluded because it already has its own unpooled data source in that sum. The minimum pool size rises from 2 to 4 so the limit is at least 1. This also settles the cross check's open "a third of the pool" item.
- **Dead marking on every wake (review Nit 5)**: run it on the poll tick only, before the schedule tick, and once per `JobsDriver` pass (CLM-1, WAK-2, JTS-2). The claim already skips a row on its last attempt, so the dead marking is bookkeeping, and doing it on every signal or freed slot wake costs a transaction on the hottest path. Runner up: send both statements in one batch and one transaction, which saves the round trip but still runs the update on every wake and holds the dead rows' locks for the claim's duration.
- **CLM-1 SQL (verify correction 1)**: PostgreSQL rejects `FOR UPDATE` in a `UNION` branch even when parenthesized (`0A000`). The spec now shows the built form, each branch inside its own derived table, which the verify run checked is equivalent on the live schema.
- **OUT-3 random source (verify correction 2)**: `System.Random` is banned repo wide, so the build uses `RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue`, uniform over [0, 1). The spec now names it.
- **Recorded from #56**: the rules now state what the review fixes built: a `Timeout` above the timer limit means no deadline (JQ-5, OUT-2), any early exception is a failed attempt and frees the slot (OUT-2), the 10 second claim bound (OUT-6), `write_failed` counted in no outcome (OUT-7), the `unknown` kind tag (JTL-1), the `+ 120` cancel callback log (JTL-2), the `RequiresProject` schedule refusal (SCH-1, AC-24), and the `JobAccessScope.Establish` caller test (JTS-4).

Spec 0007 JOB-1 (the dedupe index and `ON CONFLICT`), JOB-4 (half the pool less one), and its configuration lines were updated in the same change.

## Milestone 3: module outbox (2026-10-20)

### Context

Spec 0007 EVT-1 to EVT-4 settled the outbox's behavior: a `<module>.outbox` table per module through one shared EF configuration, versioned event records in the publisher's `Contracts/`, `IEventHandler<TEvent>` subscribers, a worker role dispatcher woken by the `job` signal and the poll that claims 100 rows at a time with `SKIP LOCKED`, enqueues one job per subscriber with `dedupe_key = '<event id>:<subscriber>'`, and marks the rows in one transaction, at least once delivery with no ordering, and a 7 day cleanup. Spec 0002 LAY-4 says the event is written in the same `SaveChanges` as the change. Milestone 2 left two hooks: `WorkerWake` (WAK-5), so a second worker loop shares the runner's wakes, and ENQ-1 on any platform context.

What was still open, and what `/develop` would otherwise have invented: how the event joins the caller's transaction, what a subscriber's job looks like so the job queue's registry and dedupe accept it (the dedupe key is now scoped per kind and project, JDB-2), how the dispatcher names each module's table in SQL when Infrastructure cannot reference module code and DAT-6 bans raw SQL, what the dispatcher's loop costs the pool under JOP-2's half pool rule, how the cleanup finds old rows without a second index, and how tests drive it all.

### Options considered (how an event joins the caller's transaction)

#### Option O1: Add a tracked `OutboxEvent` to the caller's context, saved by the caller's `SaveChanges` (chosen)

`Publish` adds the entity and enlists `job`; the caller's `SaveChangesAsync` inserts it with the change; a before commit check refuses a commit with the event still unsaved.

- Pros: exactly spec 0007 EVT-1 and LAY-4's "same `SaveChanges`"; one round trip with the change (EF batches the insert); no SQL text naming the module's table at publish time; the module's own EF model creates and describes the table; retries need nothing new (SND-6 clears the tracker and reruns `work`).
- Cons: an event can be added and never saved, so a before commit check (PUB-3) is needed to turn a silent loss into a failed commit; `Publish` cannot report a dedupe or write failure (there is none to report: events have no dedupe).

#### Option O2: SQL insert on the caller's connection at call time, like ENQ-1

- Pros: the same shape as `IJobQueue`; no unsaved state to guard.
- Cons: needs the module's table name spliced into SQL at every publish (DAT-6), one round trip per event, and contradicts EVT-1's "same `SaveChanges`" wording for no gain.

#### Option O3: No outbox; `Publish` enqueues one job per subscriber directly in the caller's transaction

- Pros: no table, no dispatcher, no extra loop connection; delivery starts one step sooner.
- Cons: the request pays one insert per subscriber; there is no event stream for EVT-4's readers (realtime, webhooks, replay); it reverses spec 0007's decision.

### Options considered (what a subscriber's delivery job is)

#### Option S1: One job kind per subscriber, the event copied into the job (chosen)

- Pros: per subscriber lease, attempts, and timeout (`JobKindOptions`), per subscriber metrics and dead jobs for free; the dedupe key `<event id>:<subscriber>` under JDB-2's `(kind, project_id, dedupe_key)` scope is unique per event and subscriber; the job never reads the outbox row, so it survives the 7 day cleanup and a dead delivery can be retried later (feature 46).
- Cons: the event is stored once per subscriber until cleanup; renaming a subscriber is a new subscriber.

#### Option S2: One kind `outbox.deliver` with the subscriber in the payload

- Pros: one registration.
- Cons: one lease, attempt count, and timeout for every subscriber; metrics and dead jobs cannot tell subscribers apart.

#### Option S3: The job holds only the event id and reads the outbox row

- Pros: no copy.
- Cons: a retry after the cleanup finds nothing; the handler reads another module's schema through Infrastructure, and the cleanup must wait for every delivery.

### Options considered (the dispatcher's pool connection)

#### Option P1: Count it, and lower JOP-2's limit by one (chosen)

- Pros: the same plain arithmetic as JOP-4 (each loop holds one connection, serially); the two loops never wait on each other.
- Cons: one fewer job slot per worker at the default pool (13, not 14), and a pool of 4 or 5 no longer starts; spec 0007 JOB-4 is amended a second time.

#### Option P2: A shared gate so both loops together use one connection

- Pros: JOP-2's numbers stay.
- Cons: a dispatch batch (up to 10 s) delays a claim, and the claim's own bound then counts waiting time; a new coordination class for one connection.

#### Option P3: Run the dispatch inside the runner's loop

- Pros: no new loop, no new connection.
- Cons: WAK-5 was built so this is a separate loop; a slow outbox would delay every claim, and the runner gains a second job.

### Options considered (naming each outbox table in SQL)

#### Option I1: One pattern checked identifier spliced through `FormattableStringFactory` (chosen)

- Pros: every value stays a parameter; the schema comes only from `AddOutbox`, checked against `^[a-z][a-z0-9_]{1,30}$` and quoted; one helper, one deviation comment per call site.
- Cons: a narrow deviation from DAT-6, amended there so reviewers find it.

#### Option I2: A PL/pgSQL function in the `jobs` schema that builds the SQL with `format('%I')`

- Pros: no splicing in C#.
- Cons: a stored function in a migration, owned by `jobs` but reading every module's schema; harder to test and to change.

#### Option I3: A per schema `DbContext` and LINQ

- Pros: no SQL text.
- Cons: EF has no `FOR UPDATE SKIP LOCKED`, so the claim still needs SQL with the table name.

### Rationale

O1 follows the settled wording (EVT-1, LAY-4) and needs the least new SQL; its one weakness, an event added but not saved, becomes a loud failed commit through a small, general hook (`AddBeforeCommit`). S1 is what makes the job queue's existing machinery (per kind options, dedupe scope, metrics, dead jobs, feature 46's retry) apply to subscribers unchanged, and copying the event keeps deliveries independent of the cleanup. P1 keeps the pool arithmetic honest at the cost of one slot. I1 is the smallest safe way to name a table Infrastructure does not own.

### Calls made while writing (recommend items, milestone 3)

The engineer was not in this run (another session started it), so each pick is the recommendation and is overridable.

- **Publish signature**: `Guid Publish<TEvent>(DbContext, TEvent, Guid? projectId)`, synchronous, with a required project argument so every caller decides; the project becomes the delivery's project and scope. Runner up: an options record like `EnqueueOptions` (nothing else to put in it yet).
- **Event type pattern**: `<schema>.<name segments>.v<n>`, first segment the publishing schema, checked at registration and at publish. Runner up: any JQ-1 name (loses the "own events only" check).
- **Registration**: `AddOutbox(schema, builder => builder.Event<T>(jsonTypeInfo))` registers the schema and its events together; `IOutboxRegistry` is the read side. Spec 0007 EVT-1's "registers its schema with `IOutboxRegistry`" is reworded to match (DI cannot hand out a service during registration).
- **Subscriber options**: `AddEventHandler` takes `JobKindOptions` like `AddJobHandler`, and refuses `RequiresProject` so an instance wide event cannot die unrun.
- **Payload limit**: 64,000 bytes per event, so the delivery envelope fits ENQ-3's 65,536.
- **Batch and bound**: 100 rows (EVT-3) and a 10 second bound per batch, the same number as `ClaimBound`; a cut batch rolls back whole, so the bound costs only time.
- **Rounds**: one batch per outbox per round, more rounds while any batch was full, so one busy module cannot starve the others.
- **Unknown types**: marked dispatched with a Warning instead of left pending, so a removed type cannot block its outbox's partial index forever. Runner up: leave them pending (they would be claimed and skipped on every pass).
- **Wake signal**: reuse `job` (EVT-3) and accept one empty pass per dispatch. Runner up: a new `outbox` signal kind (changes BUS-1 and the listener).
- **Cleanup by primary key**: `id < Floor(now − 7 days)` with `dispatched_at IS NOT NULL`, so no index on `dispatched_at`; retention is measured from `occurred_at`, which differs from "7 days after dispatch" only by the dispatch delay. Runner up: an index on `dispatched_at` (one more index write per dispatch).
- **Cleanup schedule**: `37 3 * * *`, 20 minutes after `jobs.cleanup`, `MaxAttempts` 3, like SCH-7.
- **`trace_parent` column**: added to EVT-1's list so a subscriber's trace links to the publishing request (JTL-3's pattern).
- **Test outbox**: a `sample` schema context in `Orvano.Testing`, because no module owns an outbox yet.

Spec 0007 EVT-1 to EVT-3 and JOB-4 (half the pool less two), its configuration lines, and spec 0002 DAT-6 (the identifier deviation) were updated in the same change.

### Cross check of milestone 3 (2026-10-20)

A read only pass on another model checked commit 049d35e against spec 0007 EVT-1 to EVT-4, spec 0011, spec 0002 DAT-6, and the code built for milestones 1 and 2. It confirmed the pool arithmetic (one connection per dispatch batch, `Concurrency` + 2 ≤ `PoolSize` / 2 steady, 2 × `Concurrency` + 2 ≤ `PoolSize` − 2 worst case), the per subscriber dedupe key under JDB-2's index and the built `ON CONFLICT` clause, and that the only unbounded await is `InTransactionAsync`'s rollback. Changes folded in:

- **Handler resolution (A)**: registering every subscriber as `IEventHandler<TEvent>` would make two subscribers of one type share the last registration; each handler is now registered and resolved by its own type (OBX-5, DLV-5).
- **Driver reach (A)**: a hosted service cannot be resolved, so the dispatch work is an `OutboxDispatch` singleton in `AddInfrastructure`, and `OutboxDispatcher` is only the loop (DSP-1, OTS-2, step 27).
- **Named mechanics (B)**: the delivery's `JobRegistration` comes from `JobRegistry.Find`; the internal envelope needs `InternalsVisibleTo` for Infrastructure (DSP-3, DLV-2, step 24).
- **Bounds and shutdown (B)**: the rollback is bounded only by Npgsql's command timeout, stated and accepted; the dispatcher is registered before the runner so the runner stops first and keeps the shutdown budget for its releases (DSP-1, DSP-5); a cut inside the commit is an unknown outcome, not a rollback (DSP-5, AC-36).
- **PUB-3 against the built code (B)**: `IsEmpty` counts the checks, the closure captures the context, a failing check runs the after commit actions but not the on committed ones, and retries re-add it.
- **Sample context (B)**: created by `GenerateCreateScript()`, checked by the spec 0011 rules explicitly, kept off the OGR-3 host (OTS-3, step 25).
- **Deleted projects (B)**: undispatched events of a deleted project still dispatch; handlers refuse a deleted project as AC-30 already requires (security model).
- **Old limit tests (B)**: step 26 names the tests that encode `PoolSize` / 2 − 1.
- **Notes (C)**: `\z` in the patterns; a rerun publish after an unknown commit makes a second event id (DLV-6); per outbox try and catch in the cleanup (OCL-1); database row order and the `SqlQuery` wrap (DSP-3, DSP-8); the source scan's false positives accepted (OGR-2); the future DAT-2a exemption (OTB-1); AC-31's contract test and AC-35's startup hook entries (steps 24, 25).

Left open for the engineer (Follow-up): a batch that always fails or always outruns `BatchBound` holds its outbox's head. Options are halving the batch after a failure (lean), one multi row insert per batch, or accepting it.

Settled the same day by the engineer, who approved milestone 3: option (a). Halving per outbox shrinks a failing batch until its one bad row stands alone, so the rows before it get dispatched and the fix costs no new statement shape; the sizes are in memory per worker, since a restart losing them only means starting at 100 again. A multi row insert stays an open performance follow up, independent of this.
