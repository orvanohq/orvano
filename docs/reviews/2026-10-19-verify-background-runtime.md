# /check verify background runtime · FAIL (AC-8, a silent signal connection is never timed out)

_Feature 61, milestone 1 (signals and cache invalidation), spec [0013](../specs/0013-background-runtime/index.md), on
`main` at `990654c` (built in PR #43, `4076b5d`), 2026-10-19. No `verify.md` exists beside the spec, so the checks come
from its `## Requirements`. Run from `server/` with Docker 28.1.1: the suite on its PostgreSQL 18 Testcontainer, and every
probe on a throwaway `postgres:18.6` container on port 55432, removed afterwards._

**12 of 13 acceptance criteria are met. AC-8 fails in one case: when the listener's connection goes silent without
closing, the instance stays "connected" far past `HeartbeatTimeout`, and its cache keeps serving entries other instances
have invalidated.** A dropped connection (`pg_terminate_backend`, a reset socket) is handled exactly as specced; only the
heartbeat timeout path is broken, and only when the heartbeat send itself hangs.

Next: run `/debug background runtime` for AC-8 (details below). The test session, the fresh model review, and `/sync` can
go ahead as planned; the review may want to look at this finding too.

The scope's `Verify it` box for feature 61 in [foundations.md](../scope/foundations.md) is **not ticked**, for two
reasons: this verify fails AC-8, and the feature still has two milestones to build (the job queue and the outbox), so the
box waits for those anyway. Nothing else was ticked; this run touched only `docs/reviews/`.

## Failing

**AC-8, heartbeat timeout on a silent connection.** I put a small TCP proxy between one probe host's listener and
PostgreSQL, then froze the proxy (`kill -STOP`), so the socket stayed open but no bytes moved. Settings:
`HeartbeatInterval` 2 s, `HeartbeatTimeout` 5 s.

| Time after freeze | `IsConnected` | What happened                                                                                            |
| ----------------- | ------------- | -------------------------------------------------------------------------------------------------------- |
| 1 s to 61 s       | `true`        | no `hb` arrives, yet no disconnect; no `OnDisconnectedAsync`, no fallback poll, cache not bypassed       |
| 9 s               | `true`        | host B committed an invalidation of tag `t10`; the frozen host still served the old `t10` entry          |
| 62 s              | `false`       | Warning 10002 "disconnected (connection lost)", inner `TimeoutException: Timeout during reading attempt` |

The stack in the log shows why: the read loop was stuck inside the heartbeat send
(`SignalListener.ReadAsync` at `SignalListener.cs:214`, into `NotifyHeartbeatSender.SendAsync` at
`IHeartbeatSender.cs:21`). `SELECT pg_notify(...)` waits for a reply that never comes, so the loop never gets back to
its deadline check, and only Npgsql's own command timeout (30 s, plus its cancel attempt) ends the wait. The timeout the
spec names (`HeartbeatTimeout`, LSN-3) never fires in this case; detection time comes from Npgsql, not from
`SignalOptions`.

With the default options (interval 30 s, timeout 90 s) detection lands near 90 s by coincidence, so the effect in a
default install is small. With any shorter timeout an operator is allowed to set (for example interval 5 s, timeout
10 s), the instance keeps serving stale entries for about a minute. A frozen proxy, a stuck PgBouncer, or a half open TCP
connection all look like this.

The suite misses it because `No_heartbeat_for_the_timeout_counts_as_a_disconnect` uses the no op `IHeartbeatSender`, so
the send never blocks. A suggestion for `/debug`: bound each heartbeat send by the time left until the timeout deadline
(a linked token or `CommandTimeout`), and treat that cancel as a disconnect. For `/test`: an `IHeartbeatSender` that
never returns should still give a disconnect within `HeartbeatTimeout`.

## Conformance per AC

Suite: `dotnet build Orvano.slnx -p:CI=true --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
`dotnet test --solution Orvano.slnx` → 585 of 585 passed. Trait counts are from
`dotnet test --solution Orvano.slnx --no-build --filter-trait "Spec=0013:AC-N"`, all passing. "Probe" rows are this
run's own inputs, described in the next section.

| AC    | Verdict     | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ----- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-1  | met ✅      | No AC-1 trait (0 tests). The public types and signatures in `Orvano.SharedKernel/Signals/` match the API surface table; the probe host compiled against the built `Orvano.SharedKernel.dll` and `Orvano.Infrastructure.dll` using `SignalKind`, `ICacheInvalidator`, `ISignalHandler`, `ISignalConnectionObserver`, `ISignalConnectionState`, `AddSignalHandler<T>`, `AddSignalConnectionObserver<T>`, `AddInfrastructure`, `AddSignalListener`. `Orvano.ArchitectureTests` 31 of 31, including `Module_does_not_reference_Infrastructure_or_Host_when_built` |
| AC-2  | met ✅      | Trait 3 of 3 (commit, rollback after `ExecuteUpdate`, raw SQL). Probe: a raw `LISTEN` tap saw nothing during a 2 s hold after enlisting, then one notification `{"v":1,"k":"cache","d":["t2","t9","t8"]}` for enlisted `t2,t9,t2,t8` (deduped, first order) 26 ms before `CommitAsync` returned. A rolled back attempt: tap saw nothing, row unchanged                                                                                                                                                                                                        |
| AC-3  | met ✅      | Trait 5 of 5. Probe: no transaction → `InvalidOperationException: Signals join a transaction. Wrap the write in InTransactionAsync (spec 0013 SND-6).`; plain `UseNpgsql` context → `Only platform database contexts send signals (spec 0007 SIG-3).`; synchronous `Commit` with a batch → `Commit with CommitAsync when signals are enlisted.`, and the other host received nothing                                                                                                                                                                          |
| AC-4  | met ✅      | Trait 2 of 2 against PostgreSQL 18 (`Retry_on_the_same_connection_sends_only_the_values_of_the_attempt_that_commits`, `Work_that_throws_is_rolled_back_and_leaves_no_batch_for_the_next_transaction`), plus SND-6 tests for the change tracker clear and the nested refusal. Not probed on my own: a forced retry on one connection needs the test seams                                                                                                                                                                                                      |
| AC-5  | met ✅      | Trait 17 of 17. Probe: 60 GUID tags (36 chars) → wire payload `{"v":1,"k":"cache","d":["*"]}` (29 bytes); `not json`, `"v":2`, and kind `bogus` sent with `psql` → Warnings 10004, 10004, 10005, entry stayed cached, and the next good payload applied. `job` with values and `cache` with none → ignored at Debug (10006)                                                                                                                                                                                                                                   |
| AC-6  | met ✅      | Trait 4 of 4 (including the unknown outcome commit). Probe: the writer's read right after `CommitAsync` returned ran the factory again; a rolled back attempt left both hosts' entries cached. On a host with a listener the echo also lands before `CommitAsync` returns (26 ms), so the after commit path alone rests on `Tags_are_removed_after_the_commit_and_before_CommitAsync_returns_never_before_it`                                                                                                                                                 |
| AC-7  | met ✅      | Trait 1 of 1, plus `LabelCacheTests` 4 of 4 (spec 0012 AC-10). Probe: host A committed tag `t2`; host B's next read, 0.5 s later, ran the factory                                                                                                                                                                                                                                                                                                                                                                                                             |
| AC-8  | **fail** ❌ | Trait 3 of 3. Probe, dropped connection, all as specced: one `OnDisconnectedAsync`, a poll right away and every 1 s, 3 reads in a row each ran the factory and stored nothing, gauge 0, other host stayed connected; real `orvano serve`: `/health/ready` → `Healthy 200` before, right after, and 2 s after the terminate. **Silent connection: no disconnect for 62 s with a 5 s timeout, stale read served (see Failing)**                                                                                                                                 |
| AC-9  | met ✅      | Trait 2 of 2. Probe: a host started with its listener's route down logged attempt 1 Warning, then one Warning 10002 "(could not connect)", one `OnDisconnectedAsync`, polls from boot every 1 s; on restore it connected, `cache.clears{reason=connect}` 1, then `OnConnectedAsync`. Backoff delays seen: 0.31 s, 0.48 s, 1.34 s, 0.86 s, 2 s (the cap), 0.66 s: random and never above the cap. After reconnect the cache was cleared before reads were cached again                                                                                         |
| AC-10 | met ✅      | Trait 1 of 1 (throwing handler, registration order). Probe: real `pg_notify` `cache ["t1"]` reached both the Infrastructure handler (entry removed) and the probe's own handler; `job []` and `session ["u1"]` were received and counted (no handler for them yet, by plan)                                                                                                                                                                                                                                                                                   |
| AC-11 | met ✅      | Trait 7 of 7. Probe on real `orvano serve`: interval and timeout both `00:00:30` → exit 1, `ORVANO_SIGNALS__HEARTBEATTIMEOUT: must be at least twice ORVANO_SIGNALS__HEARTBEATINTERVAL`; `FALLBACKPOLLINTERVAL=00:00:00.5` → exit 1, `ORVANO_SIGNALS__FALLBACKPOLLINTERVAL: must be at least 1 second (00:00:01)`. Defaults confirmed in `SignalOptions.cs`                                                                                                                                                                                                   |
| AC-12 | met ✅      | Trait 2 of 2 (no listener without `AddSignalListener`, the listener replaces the state) and `CacheRemovalBanTests`. `AddSignalListener` is called only in `ServeCommand.cs:87`; `MigrateCommand.cs:49` calls `AddInfrastructure` only. `BannedSymbols.Modules.txt` lists all four removal overloads and `Directory.Build.props` adds it to `Orvano.Modules.*`                                                                                                                                                                                                 |
| AC-13 | met ✅      | Trait 3 of 3. Probe through a `MeterListener`: `signals.connected` 1, then 0 while down, then 1; `signals.received{kind=cache}` counted real signals and never heartbeats (sent every 2 s); `signals.sent{kind=cache}` rose only for commits, not for the rollback; `cache.clears{reason=connect}` 2 after one reconnect, `{reason=wildcard}` 2 on the writer (local and echo) and 1 on the other host. Logs: Information 10001 on connect, Warning 10002 on disconnect, Warning 10003 per failed attempt                                                     |

### Value sourcing

Every row was checked in the code, and the ones that can be watched were watched: the channel `orvano_signal`
(`SignalPayload.Channel`, seen by the tap); the payload collapse over 999 bytes or on `"*"` (probe); the connection and
transaction from the commit event (`SignalTransactionInterceptor`); `Action == "Commit"` for the failed commit removal;
the 60 s default expiry in `AddCache`; the bypass flag merge (`SignalAwareHybridCache.Effective`, five CAC-2 tests); the
keyed `"signals"` data source built through the shared `PlatformDatabase.ConnectionString` helper with `Pooling=false`,
`KeepAlive=30`, and `orvano-signals-<8 hex>` (seen on the real host as `orvano-signals-3a91b314` in
`pg_stat_activity`); options from `ORVANO_SIGNALS__*` (probe); the 1 s backoff base (`SignalListener.BackoffBase`); the
reserved `"*"` tag for clears; handlers filtered by `Kind` in registration order; meter `Orvano.Infrastructure` and
`LogEventIds.Infrastructure = 10000`; the injected `TimeProvider` for every wait and timer.

One row is not met in practice: **"Heartbeat, timeout … durations: `SignalOptions`"**. On a silent connection the
timeout comes from Npgsql's command timeout, not from `HeartbeatTimeout` (the AC-8 failure above).

Specced surfaces: every member in the API surface table exists. Missing: none. Not applied: none (no tables, no
migration in this milestone).

### Milestones 2 and 3

Not built, by plan, and not counted as failures. Their hooks exist and route: `SignalKind.Job` and `Session` are on the
wire, a real `job []` and `session ["u1"]` were decoded, counted, and queued to handlers of their kind (none registered
yet), and `AddSignalHandler<T>` and `AddSignalConnectionObserver<T>` worked from outside the repo in the probe host.

## How the probes ran

A scratch web app in the session scratchpad (outside the repo, deleted with it) referenced the built
`Orvano.SharedKernel.dll` and `Orvano.Infrastructure.dll`, called `AddInfrastructure()` and `AddSignalListener()`,
registered its own keyed `"signals"` data source, a `UseOrvanoConventions` context, a counting observer, a recording
`cache` handler, a raw `LISTEN` tap on its own connection, and a `MeterListener`. Three copies ran at once (hosts A, B,
and C, each with its own `orvano-signals-<name>`), with `HeartbeatInterval` 2 s, `HeartbeatTimeout` 5 s,
`FallbackPollInterval` 1 s, `ReconnectBackoffCap` 2 s. Each read went through `HybridCache.GetOrCreateAsync` with a
factory that counts its calls, so "the factory ran again" is a counter going up. Notifications and terminates were sent
with `docker exec … psql`. A Python TCP proxy in front of A's and C's listeners let me cut (kill) or silence (stop) only
the listener connection while every other database read kept working. The real `orvano serve` ran once against the same
container for the terminate and `/health/ready` check and for the bad option checks. Every process and the container
were stopped afterwards.

## For /check review and later

- An unparsable value such as `ORVANO_SIGNALS__RECONNECTBACKOFFCAP=abc` ends startup with an unhandled
  `InvalidOperationException` naming `Signals:ReconnectBackoffCap`, not the variable. `ORVANO_DATABASE__POOLSIZE=abc`
  does the same, so this is the CFG-4 helper covering only `OptionsValidationException`, older than this feature. Worth a
  follow up for every options class, not a fail of AC-11.
- A bad signal option fails inside the `SignalListener` constructor (`options.Value`) before `ValidateOnStart` runs;
  the message still names the variable, so this is cosmetic.
- The develop notes list known deviations: `SignalPayload.EncodeHeartbeat()` is public (SND-7 says internal), and the
  dispatch channel is not single reader (for `Reader.Count`). Neither changes behavior; `/architect` may want to update
  the spec wording.
- `ORVANO_SIGNALS__*` is not yet in `server/AGENTS.md` or the install guide (spec follow up, for `/sync`).
