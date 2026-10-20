# Review, background runtime: signals and cache invalidation (feature 61 milestone 1, PRs orvanohq/orvano#43 and #46), 2026-10-19

**Reviewed by**: Sonnet (fresh model; code written by Claude Opus)
**Scope**: `4076b5d~1..a9e0590` on main (PR #43 built the feature, PR #46 fixed the heartbeat hang) plus 5 uncommitted test edits and one untracked test file (`SignalBackoffTests.cs`), 51 files under `server/`. The label cache tests under `Orvano.Host.IntegrationTests/Access` are skipped on purpose.
**Verdict**: Changes requested

## Summary

You added the signal contracts, the transaction join (a per context batch that an interceptor sends just before commit), the payload codec, the `LISTEN` listener with heartbeat, timeout, backoff and a fallback poll, and a `HybridCache` decorator that bypasses the cache whenever a signal could have been missed. The design is sound and I could not break the main promises: a failed or rolled back transaction never leaves a signal behind, an unknown commit outcome never loses an invalidation, and the decorator has no read path that skips the bypass. I built the solution with `CI=true` (0 warnings) and ran the Infrastructure, SharedKernel, Architecture and Signals integration tests (45, 413, 31 and 10 passed). The one major finding is the same class of bug as the AC-8 hang: the dispatch loop has no deadline, so one slow handler or observer freezes cache invalidation while the instance still claims to be connected. The rest are hardening items and small races.

## What I checked, and what held

These are the eight areas you asked me to weigh. Where something is a finding, the number points to it below.

- **a. Concurrency in `SignalListener`.**
  - _Read loop and epochs._ The read loop is the only writer of `_lastHeartbeat`, `_lastSent`, `_pollTimer` and (through `AnnounceDisconnect` and `StopPolling`) `_epoch`, apart from the timer callback, which only reads `_epoch` and touches `_pollQueued` through `Interlocked`. A poll from an old epoch is dropped twice, once when it is queued and again when the dispatch loop dequeues it (`SignalListener.cs:324`, `:374`). That double check is what makes the race where a timer callback fires just after a connect harmless. Held.
  - _Queued poll._ There is one lost wakeup case, finding 5 (a fast flap behind a busy dispatch loop skips the "poll right away").
  - _Dispose._ `Dispose` only disposes the timer and calls `BackgroundService.Dispose`, both idempotent. The container will call it up to three times, because the same instance is registered as the hosted service, as `SignalListener` and as `ISignalConnectionState`. That is harmless. The only unsynchronized access is `_pollTimer` against `Dispose` if the host times out its stop and disposes while `ExecuteAsync` is still running, which only costs a skipped dispose. Not a finding.
  - _Use after dispose._ `ReadAsync` can return while a heartbeat send is still running on the connection, and the `await using` then disposes the connection under it. Finding 4.
  - _Unobserved tasks._ The abandoned send (finding 4), and a dispatch loop that dies without anyone noticing (finding 6).
- **b. Other awaits that could hang like the heartbeat send did.**
  - _Connect:_ `OpenConnectionAsync` is bounded only by Npgsql's default connect `Timeout` (15 s), because `PlatformDatabase` sets none (finding 8).
  - _`LISTEN`:_ bounded only by the default `CommandTimeout` (30 s). It runs before the instance counts as connected, so it cannot hold a stale "connected" state. Same finding 8.
  - _Wait for a notification:_ bounded by the `wait` argument, itself capped by the heartbeat deadline. Held.
  - _Cache clear on connect:_ in memory only, no I/O. Held.
  - _Reconnect delay:_ `Task.Delay(delay, _time, stoppingToken)`, bounded. Held.
  - _Connection dispose after a silent connection:_ non pooled, so it is a socket close. I could not make it hang by reading the code, but nothing in the test suite proves it (finding 9).
  - _**Handler and observer dispatch: unbounded.**_ Finding 1.
- **c. Connection lifetime and leaks.** Every exit path goes through the one `await using` at `SignalListener.cs:132`: failed open (nothing to dispose), failed `LISTEN`, heartbeat timeout, I/O error, shutdown. `Pooling=false` makes dispose a physical close, so nothing is returned to a pool and nothing lingers. `Notification += OnNotification` is never unsubscribed, but the connection is gone with it. The keyed data source is owned and disposed by the container. Held. The one lifetime flaw is that a send abandoned at the deadline is still running when the connection is closed (finding 4).
- **d. The commit interceptor.**
  - _Failed commit:_ `pg_notify` runs inside the transaction, so PostgreSQL drops the notification when the commit fails or the transaction aborts (for example a `40001` at `COMMIT`). No stale signal. Held.
  - _Ambiguous commit_ (connection lost after `COMMIT` was sent): if the commit went through, PostgreSQL delivers the signal to the other instances, and the writer removes locally from `TransactionFailedAsync` (`SignalTransactionInterceptor.cs:121`). If it did not, the extra local removal is harmless. No invalidation is lost. Held.
  - _Retry by the execution strategy:_ batch is cleared at start, use and rollback (`:22`, `:35`, `:92`), `InTransactionAsync` clears the change tracker, and the retry test passes on the same connection. Held.
  - _Savepoints:_ values enlisted before a savepoint rollback are still sent. That is an extra signal, never a missing one. Held.
  - _Ambient `TransactionScope`:_ EF raises no transaction events for it and `CurrentTransaction` is null, so `Enlist` throws instead of silently doing nothing. Held.
  - _Raw `NpgsqlConnection` transactions outside EF_ bypass everything. SND-5 says so, but nothing stops a module from trying (finding 10).
  - _A context that joined another context's transaction_ with `UseTransaction` can enlist without an error and then never sends (finding 3).
- **e. Can a signal carry data or secrets?** The codec and `Enlist` allow only 1 to 200 printable ASCII characters with no spaces per value (`TransactionSignals.cs:57`), the encoded payload is measured in UTF-8 bytes after the default encoder's escaping (a `+` becomes 6 bytes), and anything over 999 bytes or containing `"*"` becomes `["*"]`. That is far below the 8000 byte `NOTIFY` limit, so there is no way to reach it. A value is still just a string: nothing can tell an id from a secret, so SIG-1 depends on module authors. The receiving side does not re-check values (finding 13). Values are logged only at Debug and only after an `IsEnabled` check (`SignalListener.cs:296`). The one place wire text reaches a higher level log is the unknown kind (finding 10).
- **f. The `HybridCache` decorator.** Every `GetOrCreateAsync<T>` overload funnels into the one abstract `GetOrCreateAsync<TState, T>`, which the decorator overrides, so there is no path around it. `SetAsync` goes through `Effective` too. Removals pass through unchanged, as CAC-2 asks. `Effective` keeps the caller's expiry and flags and ORs in the bypass, with a null `Flags` falling back to the default options. With the bypass flags the stampede key differs from a connected call, so a bypassed read does not share a result with a connected one. On reconnect the order is `LISTEN`, clear, then `_connected = true`, and reads that start in between are bypassed, so nothing is stored in that gap. The one remaining window is a read that started while connected, is still running its factory when the connection drops, and stores after the reconnect clear. It needs a factory slower than the whole outage, and it is the same class of risk as CAC-4, so I left it out of the findings. Held.
- **g. Banned symbol coverage.** I copied `Directory.Build.props` and `BannedSymbols.Modules.txt` into a scratch project named `Orvano.Modules.Scratch` (outside the repo) and confirmed RS0030 fires for `RemoveByTagAsync(string)`, `RemoveByTagAsync(IEnumerable<string>)` and `RemoveAsync(string)`. The architecture test checks the list against every public removal overload by reflection, which is a good guard against a package upgrade adding one. Nothing bans the other ways around the invalidator (finding 10).
- **h. Log and metric hygiene.** Event ids are 10001 to 10011 in the new `Infrastructure = 10000` block, unique and in order. Metric tags are `kind` (the enum's wire name, so three values) and `reason` (two constants). No project or user tags. Held, with the small items in findings 2 and 13.

## Major

### 🟠 1. Handlers and observers run on one loop with no deadline, so one slow call stops cache invalidation, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:360`

**Problem**: `DispatchAsync` awaits each handler and observer in turn and only passes the host's stopping token (`:370`, `:376`, `:381`). Nothing bounds a call. This is the same shape as the AC-8 bug: an await in the listener with no deadline of its own. The cache handler lives on this loop too (`CacheSignalHandler`), and `IsConnected` stays true while the loop is stuck. LSN-6 asks for one loop and HND-4 asks handlers to be quick, but a rule in a doc is not a guard.
**Why it matters**: Today the only handler is the in memory cache removal, so this is latent. It becomes live with the next two milestones and feature 14: the session fallback poll (`OnFallbackPollAsync`) queries the database, and it runs exactly when the database is likely to be unreachable (the default `CommandTimeout` is 30 s, and polls repeat every 5 s). While it blocks, the `Connected` observer and every `cache` signal that arrives after the reconnect wait behind it, and the instance serves entries another instance already invalidated, with `IsConnected` true and the bypass off. A handler that waits on a lock or a slow query does the same. The queue is unbounded, so the only symptom is a Warning when it passes 1000 items.
**Suggested fix**: Give every handler and observer call its own bound: a linked token that cancels after a limit taken from the options, and wait on it the way `SendHeartbeatAsync` does so a call that ignores the token cannot hold the loop. On a timeout log at Error and move on. Consider a separate path for the cache handler, or treat "dispatch has not made progress for the heartbeat timeout" as a reason to mark the instance disconnected. Add a test with a handler that never returns, like the hanging heartbeat test.

## Minor

### 🟡 2. `signals.sent` counts a signal even when the commit failed, `server/src/Orvano.Infrastructure/Signals/SignalBus.cs:19`

**Problem**: The counter is an after commit action, and after commit actions also run when the commit fails with an unknown outcome (`SignalTransactionInterceptor.cs:121`). LSN-7 says only committed signals count.
**Why it matters**: A failed commit (or a `pg_notify` error that fails it) is counted as sent, so the sent versus received numbers drift apart during exactly the incidents someone would read them in.
**Suggested fix**: Count in a separate list that runs only from `TransactionCommittedAsync`, or let after commit actions know whether the commit is confirmed. Add a test next to the unknown outcome one.

### 🟡 3. A context that joins another context's transaction can enlist and silently send nothing, `server/src/Orvano.SharedKernel/Data/TransactionSignals.cs:68`

**Problem**: `Enlist` only checks that `Database.CurrentTransaction` is not null. A context that called `UseTransaction(other.Database.CurrentTransaction.GetDbTransaction())` passes that check, gets its own batch (reset by `TransactionUsedAsync`), and when the owner commits, that batch is never sent and its after commit actions never run. SND-5 documents the rule but nothing enforces it.
**Why it matters**: This is the shape of "several module contexts in one request". The module's `Invalidate` succeeds, the data changes, and no instance (including the writer) drops the entry until it expires, up to 60 seconds for security related entries. It is a silent wrong answer window instead of an exception.
**Suggested fix**: Mark the batch as "joined" in `TransactionUsed` and `TransactionUsedAsync` and make `Enlist` throw for a joined context with a message that says to enlist on the committing context. Add a test with two contexts sharing one transaction.

### 🟡 4. A send that is abandoned at the deadline keeps running on a connection that is then disposed, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:252`

**Problem**: The fix for AC-8 waits on the send with the linked token and, when the deadline passes, returns `false` while the send task is still running. The caller then leaves `ReadAsync`, the `await using` closes the connection, and the abandoned task faults later (cancelled, or a closed socket) with nobody awaiting it. It may also still call `OnNotification` and write `_lastHeartbeat` for a connection that has been replaced.
**Why it matters**: On .NET 10 an unobserved task exception does not crash the process, so this is noise and a small chance of a late heartbeat extending the new connection's deadline, not an outage. But it is the one place the listener touches a connection after giving up on it.
**Suggested fix**: Attach a continuation to the abandoned task that observes its exception (and drops it at Debug), and ignore notifications from a connection that is no longer the current one (a per connection token or generation check in `OnNotification`).

### 🟡 5. A fast reconnect flap can lose the "poll right away" after a disconnect, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:324`

**Problem**: `_pollQueued` is reset only when the dispatch loop dequeues a poll. If a poll for epoch E is still waiting behind a busy loop, the instance connects (E+1) and disconnects again (E+2), the new timer's immediate tick finds `_pollQueued == 1` and is skipped. The old item is then dropped at dequeue for its epoch, and the next poll comes one `FallbackPollInterval` later.
**Why it matters**: AC-8 says the poll runs right away on every disconnect. Here it can be late by up to 5 s, and only with a backlog and a flap, so the effect is small. It is a real lost wakeup though.
**Suggested fix**: Reset `_pollQueued` in `AnnounceDisconnect` and `StopPolling` as well as at dequeue, or key the flag by epoch.

### 🟡 6. The dispatch loop can die without anyone noticing, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:93`

**Problem**: `ExecuteAsync` starts `DispatchAsync` and only awaits it in the `finally`. Handler and observer exceptions are caught, but anything else that faults the loop (for example a logger that throws) leaves the read loop running, `IsConnected` true and no handler ever called again.
**Why it matters**: Same outcome as finding 1: a connected looking instance that applies no invalidations. The chance is low, but the failure is silent.
**Suggested fix**: Catch everything in the loop body per item (not only handler calls), or watch the dispatch task and mark the instance disconnected and restart the loop if it ends while the host is still running.

### 🟡 7. The reconnect has no floor, so a connection that dies right after `LISTEN` reconnects at full speed, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:136`

**Problem**: `failures` resets to 0 as soon as `LISTEN` succeeds, and a drop after a connect takes the immediate path (`:150`). LSN-5 says exactly this, but there is no minimum time a connection must live before the count resets.
**Why it matters**: If a connection keeps dying seconds or milliseconds after connecting (a proxy that kills it, a failing heartbeat `pg_notify` such as a full notification queue), the instance loops through connect, `LISTEN`, cache clear, Warning log and observer calls with no delay. Every pass clears the whole cache, so a flapping signal connection also turns off caching for everything.
**Suggested fix**: Reset `failures` only after the connection has been up for a stable period (for example one heartbeat interval), or apply a small minimum delay when the last connection lived less than that. Keep the first attempt after a long lived connection immediate.

### 🟡 8. Connect and `LISTEN` rely on Npgsql's default timeouts, `server/src/Orvano.Host/Database/PlatformDatabase.cs:35`

**Problem**: The signals data source sets `Pooling`, `KeepAlive` and `ApplicationName` but not `Timeout` or `CommandTimeout`, so the connect and the `LISTEN` are bounded by the library defaults (15 s and 30 s). These can change between Npgsql versions and a user's connection string options do not apply.
**Why it matters**: The AC-8 fix shows that a deadline nobody wrote down is a deadline nobody tested. If a default grows, the first connect (and the reconnect after a timeout) can hold the instance in "disconnected" longer than expected.
**Suggested fix**: Set both explicitly on the signals connection string (for example from `ReconnectBackoffCap` or a small constant), and say so in the comment next to `Pooling`.

### 🟡 9. The AC-8 regression test never touches a real silent connection, `server/tests/Orvano.Host.IntegrationTests/Signals/SignalListenerTests.cs:138`

**Problem**: `A_heartbeat_send_that_never_returns_disconnects_at_the_timeout...` uses `HangingHeartbeat`, a fake whose task never completes and ignores the token. It proves the listener bounds the send. It does not run the real `NotifyHeartbeatSender` against a connection that goes silent, so Npgsql's cancel path and the connection close with a command in flight are only covered by the manual TCP proxy run in the verify report.
**Why it matters**: The part of the bug that came from Npgsql's behavior (the command timeout, the cancel request) is exactly what the fake skips. A package upgrade could change it with no test failing.
**Suggested fix**: Add one test that puts a small TCP proxy between the listener and PostgreSQL, stops forwarding after connect, and checks the disconnect arrives at `HeartbeatTimeout` with the real sender. It can use short options and the wall clock.

### 🟡 10. Nothing keeps modules away from raw `NpgsqlConnection`, `pg_notify` or `TransactionSignals`, `server/src/BannedSymbols.Modules.txt:1`

**Problem**: Only the four `HybridCache` removal methods are banned, which is what AC-12 asks for. A module can still inject `NpgsqlDataSource` and write with its own connection (no signal, no invalidation), run `ExecuteSqlRaw("SELECT pg_notify(...)")`, open its own `LISTEN` connection, call the public `TransactionSignals.Enlist` or `AddAfterCommit`, or resolve the undecorated cache with `[FromKeyedServices("orvano:inner")] HybridCache` and skip the bypass.
**Why it matters**: Each one is a way to get the very bug the design removes (an entry that other instances never drop, or a cache read while disconnected). None is likely by accident, but the rules in the spec ("modules never touch the listener or `pg_notify`") are enforced for one path only.
**Suggested fix**: Extend the modules ban list with `NpgsqlConnection.Wait`/`WaitAsync` and `Notification`, `NpgsqlDataSource.OpenConnection*` (modules should use their `DbContext`), and `TransactionSignals` plus `SignalPayload`, or add an architecture test that modules reference none of them. Treat the keyed string `"orvano:inner"` the same way, or make the key harder to guess than a documented constant.

### 🟡 11. After commit actions are not defended inside `PendingSignals`, `server/src/Orvano.SharedKernel/Data/PendingSignals.cs:61`

**Problem**: `RunAfterCommitAsync` calls each action without a try block, and the contract "must catch its own exceptions" is the only protection. `TransactionSignals.AddAfterCommit` is public, so any module can add an action. If it throws, the exception leaves `TransactionCommittedAsync`, which EF reports as a commit failure, after the commit already happened.
**Why it matters**: `InTransactionAsync` would roll back (a no op), and the execution strategy may run `work` a second time, applying the write twice. I am reasoning from how `RelationalTransaction.CommitAsync` is structured and did not reproduce it.
**Suggested fix**: Catch and log inside `RunAfterCommitAsync` around each action, whatever the action does, and keep the "catch your own" note as the first line of defense. Add a test with a throwing action.

### 🟡 12. `SignalBus.Enlist` accepts the `cache` kind and skips the writer's local removal, `server/src/Orvano.Infrastructure/Signals/SignalBus.cs:11`

**Problem**: `ISignalBus.Enlist(db, SignalKind.Cache, tags)` sends the signal but does not add the after commit removal that `CacheInvalidator` adds. The docs tell modules to use the invalidator for cache tags, but the type does not stop them.
**Why it matters**: The writer then only drops its own entry when its own echo arrives a few milliseconds later, so a read right after `CommitAsync` can still return the old value. That breaks AC-6 for any caller who picks the wrong interface.
**Suggested fix**: Have `SignalBus.Enlist` reject `Cache` (and point to `ICacheInvalidator`), or route it to the invalidator.

### 🟡 13. Wire text is trusted on receive, and an unknown kind is logged as received, `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:284`

**Problem**: `TryDecode` checks the shape but not the values, so empty strings, spaces and very long strings pass to `RemoveByTagAsync`. `SignalKindUnknown(wireKind)` writes the raw `k` text (up to the 8000 byte limit, newlines included) at Warning.
**Why it matters**: Only a session with the owner role can send these (spec 0013 security model), so this is hygiene. A bad `k` can bloat or forge a log line, and a bad value can make the cache handler throw an Error log per signal.
**Suggested fix**: Truncate the logged kind to a short length and strip control characters, and have `TryDecode` apply the same value rule as `Enlist` (drop the signal as malformed otherwise).

## Nits

- ⚪ `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:211`, `now` is read once at the top of the loop and used after the heartbeat send, so a slow send lengthens the next wait by the send time (at most one interval past the deadline's intent). Read the clock again after the send.
- ⚪ `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:161`, the first disconnect and the first poll are announced only after the first connect attempt fails (up to the 15 s connect timeout), not "from boot" as AC-9 words it. Consider announcing at the start of `ExecuteAsync`.
- ⚪ `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:412`, `IsEnabled(LogLevel.Error)` around a generated `[LoggerMessage]` call is redundant, the generator already checks.
- ⚪ `server/src/Orvano.Infrastructure/Signals/SignalListener.cs:235`, the `catch (Exception) when (MarkDisconnected())` filter works but reads like a trick. A `try`/`finally` that sets `_connected = false` on a non success path says the same thing more plainly.
- ⚪ `server/src/Orvano.Infrastructure/InfrastructureServices.cs:79`, `inner.ImplementationFactory!` throws a null reference if a future `AddHybridCache` registers an instance. The `IsKeyedService` guard above it hides that from the tests; a clear error message would help.

## Strengths

- The transaction join is built the right way round: the signal is part of the transaction, so commit, rollback and unknown outcomes fall out of PostgreSQL's own rules instead of bookkeeping, and the interceptor's failure rules (SND-3) are small and tested.
- The listener keeps the stale cache promise where it counts: bypass starts on the read loop before queued work, the clear comes before `IsConnected`, and every poll is checked against the epoch at queue time and at dequeue time.
- `SignalPayload` measures real encoded bytes after escaping, folds oversize into `["*"]` for both the wire and the local removal, and has no way to exceed `NOTIFY`'s limit.
- The architecture test that lists every public `HybridCache` removal overload by reflection will catch a package upgrade that adds one, and the ban does fire in a module project (I checked).
- The AC-8 fix is tested on a clock only the test moves (`ManualClock`), so the deadline test is exact and not flaky.

## Test coverage

Strong overall: 499 tests passed across the four projects I ran, and each AC has a named test with a `Spec` trait. Covered well: the join (commit, rollback, retry on the same connection, unknown commit outcome, two contexts in one request, `ExecuteUpdate`), the codec edges (escaping, oversize, malformed), the decorator flags, the two host cache path including the `["*"]` clear of tagged and untagged entries, reconnect, first connect failure, heartbeat timeout, handler order and throwing handlers, and the backoff bounds.

Gaps, in order of weight:

- A handler or observer that never returns (finding 1), and a dispatch loop that dies (finding 6).
- The real `NotifyHeartbeatSender` against a silent socket (finding 9).
- `UseTransaction` on a second context (finding 3), a throwing after commit action (finding 11), `ISignalBus.Enlist` with `Cache` (finding 12), and `signals.sent` on a failed commit (finding 2).
- A flapping connection and the backoff floor (finding 7).
- No test proves the modules ban fires in a compiled module project (the architecture test checks the list and the props file, not the analyzer). I confirmed it by hand, but it is worth one test project or a documented check.
