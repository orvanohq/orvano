# /check verify background runtime (module outbox) · PASS

_Feature 61, milestone 3 (module outbox and dispatcher), spec [0013](../specs/0013-background-runtime/index.md) build
plan steps 24 to 30, AC-31 to AC-43. Commit `364f715` on main (merged in orvanohq/orvano#61), 2026-10-21. Steps 1 to
12 (signals) and 13 to 23 (job queue) were verified earlier in
[2026-10-19-verify-background-runtime.md](2026-10-19-verify-background-runtime.md) and
[2026-10-19-verify-background-runtime-job-queue.md](2026-10-19-verify-background-runtime-job-queue.md) and are not
repeated here. No `verify.md` exists beside the spec, so the checks come from its `## Requirements`, the *Outbox value
sourcing* table, the *Outbox invariants*, and the *Outbox critical test scenarios*. Run from `server/` with Docker on a
10 core Mac: the suite on its PostgreSQL 18 Testcontainer, and every probe on a throwaway `postgres:18.6` container on
port 55432 with `log_statement=all`, removed afterwards._

**All 13 acceptance criteria (AC-31 to AC-43) are met, each seen in the real `orvano` process, the suite, or both.
Nothing failed.** The real process walked DSP-5's batch halving exactly as the spec's scenario describes, for a batch
that throws and for one cut by `BatchBound`, and reset to 100 after the next success. The one conflict /develop settled
in code (the DLV-2 envelope written without `InternalsVisibleTo`) holds up and is listed below with three smaller spec
corrections.

Next: `/test background runtime` (the `Test it` step), then a fresh model `/check review`, then `/sync` and
`/document`. You may also want `/architect background runtime: DLV-2 envelope, AC-32 pending changes, OBX-7 type in
two outboxes, AC-37 fallback timing` to fold the corrections into the spec; none blocks the next steps.

Scope and spec: ticked `Verify it` for feature 61 in [foundations.md](../scope/foundations.md), since all three
milestones (signals, job queue, outbox) are now verified. There is no `verify.md` to tick. The spec stays
`In Progress` until the remaining boxes are done.

## Failing

None.

## Spec corrections for /architect

1. **DLV-2 asks for `InternalsVisibleTo` that spec 0012 RSV-1 forbids.** DLV-2 (and build plan step 24) says
   `Orvano.SharedKernel.csproj` adds `InternalsVisibleTo` for `Orvano.Infrastructure`, so the dispatcher can serialize
   the internal `OutboxDelivery` through the internal `OutboxJsonContext`. Spec 0012 RSV-1 (MUST) and its architecture
   test keep SharedKernel's internals visible to test projects only. The build follows RSV-1: the only
   `InternalsVisibleTo` in `Orvano.SharedKernel.csproj` is `Orvano.SharedKernel.Tests`, and
   `OutboxDispatch.Envelope` (`server/src/Orvano.Infrastructure/Outbox/OutboxDispatch.cs:237`) writes the same JSON
   with a `Utf8JsonWriter` (`WriteRawValue` of the row's payload), pinned by a unit test that the subscriber's kind
   reads it. In the real process the envelope came out as DLV-2 shows it, camelCase, with the row's payload as
   `data`: `{"data": {"Text": "hello", "NoteId": "…"}, "type": "probe.note.added.v1", "eventId": "…", "occurredAt":
   "2026-10-21T06:55:28.426802+00:00"}`, and both subscribers read it back into an equal event. Suggested spec change:
   drop the `InternalsVisibleTo` sentence from DLV-2 and step 24, and name the writer in Infrastructure.

2. **AC-32's "`HasPendingModelChanges()` is false" cannot apply yet.** No module owns an outbox, so there is no
   migration to compare with; the test only `SampleOutboxDbContext` creates its tables from `GenerateCreateScript()`
   (OTS-3), which has no migration either. The live shape is right (below), and the spec 0011 rules run on the sample
   model. Suggested spec change: say the pending changes check applies to the first module that calls `HasOutbox()`
   (features 13 and 14), and that this milestone checks the shape through the sample context.

3. **OBX-7's "an event type registered by two outboxes" is reachable only through a schema registered twice.**
   OBX-3 makes each type's first segment equal its outbox's schema, so two different outboxes can never register the
   same type. The `type-in-two-outboxes` registration (two `AddSampleOutbox()` calls) prints the schema twice problem
   and then `The event type sample.note.added.v1 is registered by the sample and sample outboxes (spec 0013 OBX-7).`
   Harmless, but the spec lists it as its own case. Suggested spec change: say the type check is a backstop that
   fires only alongside the schema twice problem.

4. **AC-37's fallback takes two polls, by design, which the spec could say.** With B's listener down, the dispatcher
   ran on B's poll tick, but its own commit's `job` signal could not reach B's runner, whose claim on that same tick
   had already run; the deliveries waited for the next poll. Observed: 1 minute 48 seconds at `PollInterval` 1 minute,
   inside the "two `PollInterval`s" AC-37 allows. Suggested spec change: one line in DSP-1 or WAK-2 that a dispatch on
   a poll tick is claimed on the next one when the listener is down.

## Conformance per AC

Suite: `dotnet build Orvano.slnx -p:CI=true --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
`dotnet test --solution Orvano.slnx --no-build` → 877 of 877 passed (Architecture, Infrastructure, Auth, SharedKernel,
Host integration). Trait counts are the `[Trait("Spec", "0013:AC-N")]` tags in `tests/`, all in that passing run.
"Probe" rows are this run's own inputs against the real process, described in _How the probes ran_.

| AC    | Verdict | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ----- | ------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-31 | met ✅  | Trait 4. The probe hook compiled against the built `Orvano.SharedKernel.dll` using `IOutboxEvent` (static `Type`), `IOutbox.Publish`, `IEventHandler<TEvent>`, `EventContext`, `OutboxBuilder.Event`, `AddOutbox`, `AddEventHandler<TEvent, THandler>`, `HasOutbox()`, and `Entity`; `IOutboxRegistry`, `OutboxRegistration`, `OutboxEventRegistration`, `EventSubscription`, and `OutboxEvent` are public in `Outbox/`. The real host resolved `IOutbox` and ran the handlers. No `Orvano.Modules.*` project references `Orvano.Infrastructure`, and the architecture tests passed                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| AC-32 | met ✅  | Trait 3. Probe: `\d+ probe.outbox` (created from the context's `GenerateCreateScript()`) shows the 7 columns of _Outbox table_ with matching types and nullability, `pk_outbox`, `ix_outbox_pending btree (id) WHERE dispatched_at IS NULL`, the CHECK constraints `ck_outbox_type` (`char_length(type) >= 3 AND <= 100`) and `ck_outbox_trace_parent` (`<= 55`), and `Options: autovacuum_vacuum_scale_factor=0.05`. The pending changes part waits for a real migration (correction 2)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| AC-33 | met ✅  | Trait 3. Probe on host A: a transaction added 3 notes and published 3 events, then held 3 s. From another connection at mid hold: outbox 1, notes 1 (the earlier rows); after: 4 and 4. The `LISTEN` tap saw one `job` notification from A's backend for the three events. A rolled back twin (`SaveChangesAsync`, then a throw): the database log shows `INSERT INTO probe.note`, `INSERT INTO probe.outbox`, `ROLLBACK`; no row, no signal, never dispatched. Returned id equals the row's id (`01a15d3e-54aa-7a3b-…`), and the id's timestamp (06:55:28.426) is `occurred_at` (06:55:28.426802) to the millisecond. Retry under the execution strategy and the `ManualClock` match: trait tests in `OutboxPublishTests`                                                                                                                                                                                                                                                                                                                                                                                                 |
| AC-34 | met ✅  | Trait 3. Probe `/probe/refuse`, each with nothing tracked after: no transaction → `Signals join a transaction. Wrap the write in InTransactionAsync (spec 0013 SND-6).`; joined context → `This context joined another context's transaction with UseTransaction. …`; plain `UseNpgsql` context → `Only platform database contexts send signals (spec 0007 SIG-3).`; unregistered type → `No outbox registers the event type 'probe.not.registered.v1'. Add it with services.AddOutbox (spec 0013 OBX-3).`; another outbox's type → `Publish probe2.ping.done.v1 on the probe2 module's own DbContext (spec 0013 PUB-2).`; context without `HasOutbox()` → `The probe3 context has no outbox table. Call modelBuilder.HasOutbox() in OnModelCreating (spec 0013 OTB-2).`; `Guid.Empty` and a 70 KB payload → `ArgumentException` naming PUB-2. PUB-3: publish with no `SaveChangesAsync` → `InvalidOperationException: An event published through IOutbox was not saved. Call SaveChangesAsync before the commit (spec 0013 PUB-3).`; the database log shows `BEGIN`, `ROLLBACK` and no `pg_notify`; the tap saw no signal |
| AC-35 | met ✅  | Trait 9. Probe, real `serve` with the repo's `Orvano.Testing` startup hook (`TESTHOOK_OUTBOX`), roles `api` and `worker`, each exit 1 with one stderr line: `The outbox of schema sample is registered more than once (spec 0013 OBX-7).`; the type in two outboxes (correction 3); `The event subscriber hook.subscriber subscribes to sample.note.added.v1, which no outbox registers …`; `… reads sample.note.added.v1 as LookalikeNoteAdded, but its outbox registers it as SampleNoteAdded …`; a subscriber named like a kind → `The job kind hook.job is registered more than once (spec 0013 SCH-1).` The `ConfigurationException` cases (bad schema, bad type, wrong first segment, type added twice, `RequiresProject`): unit tests in `OutboxRegistrationTests`                                                                                                                                                                                                                                                                                                                                                  |
| AC-36 | met ✅  | Trait 6. Probe on B: one event with two subscribers → two jobs, kind = subscriber, `project_id` = the event's, `dedupe_key` `01a15d3e-54aa-7a3b-b1ad-e77095fce5b3:probe.note_added.record_a` and `…:record_b`, the row's `trace_parent`, `dispatched_at` set in the same commit. Select is `… ORDER BY id LIMIT $1 FOR UPDATE SKIP LOCKED` on `"probe".outbox`. Setting `dispatched_at` back to null and waking: marked again, the insert hit `ON CONFLICT (kind, project_id, dedupe_key) … DO NOTHING`, still 2 jobs, 2 handler runs. Unknown type row inserted by `psql`: marked, no job, one Warning. Batch halving and reset: see _DSP-5 in the real process_ below. Two dispatchers (B and C) on 300 pending events: four batches on four backends (99, 100, 100, 1), 300 jobs per subscriber, 300 distinct keys each, no event handled twice, deliveries split across both workers (a 157/143, b 146/154). Another outbox (`probe2`) kept dispatching at 100 while `probe` failed every pass                                                                                                                         |
| AC-37 | met ✅  | Trait 1. Probe, `PollInterval` 1 minute on both hosts: commit on A at 06:55:28.505 (database log) → dispatched on B at 06:55:28.534 → both handlers on B (pid 5761) at 06:55:28.577, 72 ms after the commit. With B's listener backend terminated and held down by a login event trigger: commit on A at 06:59:41.106, dispatched on B's poll at 07:00:28.989, handlers on the next poll at 07:01:29.031, 1 minute 48 seconds, inside two `PollInterval`s (correction 4)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| AC-38 | met ✅  | Trait 7. Probe handler rows: `ctx_event_id` = the outbox id, `ctx_type` `probe.note.added.v1`, `ctx_occurred_at` = the row's, `ctx_project` and `IProjectContext.Id` both `11111111-…`, caller `System` and the same instance as `Caller.System`, attempt 1; the event read back equal (note id and text). Outcomes on B, `record_a` with `MaxAttempts` 2: `THROW` → `queued` attempt 1, retry 6.6 s later, then `dead` at 2 of 2 with `InvalidOperationException: probe throw` while `record_b` of the same event `succeeded`; `PERM` → `dead` at attempt 1 with `PermanentJobFailureException`, `record_b` `succeeded`; a row whose `NoteId` is `"not-a-guid"` → `JsonException … Path: $.NoteId` as a failed attempt, `dead` at 2 of 2 for `record_a`, still retrying for `record_b` (10 attempts). A project less event reading `IProjectContext.Id` throwing: trait test                                                                                                                                                                                                                                              |
| AC-39 | met ✅  | Trait 5. Probe: 12,000 dispatched rows 8 days old, 5 dispatched rows 6 days old, 1 undispatched row 30 days old (held under a row lock so the live dispatcher skipped it), 3 dispatched rows 9 days old in `probe2`; `outbox.cleanup` made due → ran on B's poll at 07:02:29, `succeeded` at attempt 1 of 3. Information logs: `deleted 12000 … from the probe outbox`, `3 … probe2`, `0 … probe3`. Database log: three `DELETE FROM "probe".outbox WHERE id IN (… LIMIT 5000)` statements, then one each for `probe2` and `probe3`, boundary `01a13938-3b77-7000-8000-000000000000` (version 7, random bits zero, timestamp 2026-10-14 07:02:28.983 = now − 7 days). After: the 5 newer rows and the undispatched row remain. `EXPLAIN` of the inner select: `Index Scan using pk_outbox … Index Cond: (id < …)`. Schedule row: `37 3 * * *`, next 03:37 UTC                                                                                                                                                                                                                                                              |
| AC-40 | met ✅  | Trait 4. Probe, real `serve` role `worker` on 10 cores: pool 5 → exit 1, `ORVANO_DATABASE__POOLSIZE: The field DatabaseOptions.PoolSize must be between 6 and 1000`; pool 30 with concurrency 14 → exit 1, `ORVANO_JOBS__CONCURRENCY (14) must be at most 13: half of ORVANO_DATABASE__POOLSIZE (30) less two connections for the job runner and the outbox dispatcher (spec 0007 JOB-4).` (JOP-2's text); pool 10 with 4 → `… at most 3 …`; pool 30 with 13 → starts, `Job runner started: concurrency 13`; defaults: pool 30 → 13, pool 6 → 1. The headroom with every handler, renewal, and a dispatch batch at once: trait test (_Pool headroom_ with a stalled dispatch batch)                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| AC-41 | met ✅  | Trait 6. Probe logs: event ids 10200 (dispatcher started, Information, `3 outboxes and 3 subscribers`), 10202 (batch failed, Error, 37 times), 10203 (unknown type, Warning), 10205 (cleanup, Information); 10201 and 10204 are Debug and below the configured level. No Orvano line at Information or above held a payload value (the only matches were the JSON path `$.NoteId` in an exception message). `trace_parent` copied from A's request activity onto the outbox row and each delivery job. Metric names, tags, and the dispatch activity are seen only through the suite's `MeterRecorder` and activity tests (no exporter in this run)                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| AC-42 | met ✅  | Trait 6. `BannedSymbols.Modules.txt` line 20 bans `T:Orvano.SharedKernel.Outbox.OutboxEvent`; `ModuleIsolationTests` (the `<schema>.outbox` scan) and the OGR-3 registered outbox test passed. Probe: host A (`api`) and `orvano migrate` with the probe hook logged no `Outbox dispatcher started`; only `serve` with `worker` did. A published from the `api` role                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| AC-43 | met ✅  | Trait 3. A grep of `Infrastructure/Outbox/*.cs` finds no `now()`, `clock_timestamp`, `CURRENT_TIMESTAMP`, or `DateTime*.Now`; the database log shows `dispatched_at`, the cleanup boundary, and the batch size passed as parameters. `JobsDriver.DispatchAsync` and the chain through `RunDueAsync`: trait tests in `OutboxDispatchTests` and `OutboxDeliveryTests`; the real process cannot take a manual clock, so the driver half of this criterion rests on the suite                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |

### DSP-5 in the real process

Each row is one dispatch select on `"probe".outbox`, read from the PostgreSQL log (`LIMIT $1` and its parameter), with
B on a 2 s poll. 120 pending events, the 7th in `id` order carrying `POISON`, and a `BEFORE INSERT` trigger on
`jobs.job` that raises for it:

| Time         | Size | Outcome                                                                      |
| ------------ | ---- | ---------------------------------------------------------------------------- |
| 06:57:02.275 | 100  | failed (Error 10202, `probe poison in 01a15d3f-…:probe.note_added.record_a`) |
| 06:57:02.368 | 50   | failed                                                                       |
| 06:57:04.227 | 25   | failed                                                                       |
| 06:57:06.220 | 12   | failed                                                                       |
| 06:57:08.217 | 6    | committed: the six events before the poison dispatched                       |
| 06:57:08.263 | 100  | failed (next round of the same pass, so the size had reset)                  |
| 06:57:08.270 | 50   | failed, then 25, 12, 6, 3 on the next four polls                             |
| 06:57:18.221 | 1    | failed, and again at 1 on every pass after                                   |
| 06:57:46.217 | 1    | committed, after the trigger was dropped                                     |
| 06:57:46.246 | 100  | committed; the backlog drained, 124 events and 248 deliveries in all         |

`probe2` selected at 100 on every pass throughout, and a ping published there at 06:57:14 was delivered at 06:57:18.
With a trigger that sleeps 12 s on a `SLOW` payload, each batch was cancelled 10.0 s after its select (`canceling
statement due to user request` at 06:58:18.863 and 06:58:28.888, for selects at 06:58:08.849 and 06:58:18.883), and
the size went 100, 50, 25, 12, 6; the size 3 batch after the trigger was dropped committed at 06:58:59.005 and the next
select was 100. At B's very first start the probe tables did not exist yet, so two failed passes took every outbox to 25
before the first success reset it to 100: the same rule on a third kind of failure. A restarted B started at 100 again.

## How the probes ran

A small startup hook (`DOTNET_STARTUP_HOOKS`, in the session scratchpad outside the repo and deleted with it) loaded
into the real `orvano serve` process, built from this commit's `Orvano.Host` output. It registered three outboxes
(`probe` with `probe.note.added.v1`, `probe2` with `probe2.ping.done.v1`, and `probe3`, whose context has no
`HasOutbox()`), two subscribers of the note event (`probe.note_added.record_a` with `MaxAttempts` 2, which throws or
fails permanently on a marker text, and `probe.note_added.record_b`) and one of the ping, and a few `AllowPublicAccess`
GET routes that create the tables from `GenerateCreateScript()` and publish in, out of, and around `InTransactionAsync`.
Each handler wrote a row to a `probe_ran` table (event id, `EventContext` fields, `IProjectContext.Id`, caller, job id
and attempt, process id) so timing and "exactly once" came from SQL. Host A ran role `api`; hosts B and C ran `worker`.
A `psql` `LISTEN orvano_signal` tap timestamped every notification. Failures were made in the database: a `BEFORE
INSERT` trigger on `jobs.job` for the poison and slow batches, and a PostgreSQL 18 login event trigger refusing only
B's `orvano-signals-<id>` application name for the listener outage. Old rows for the cleanup were inserted with
`uuidv7(interval)`. Bad registrations used the existing `Orvano.Testing` hook (`TESTHOOK_OUTBOX`). Every process and
the container were stopped afterwards.

## For /check review and later

- A failed dispatch batch logs twice at Error: EF's own `Failed executing DbCommand` (with the statement) and the
  outbox's 10202 with the exception. On a poison row that fails every `PollInterval`, that is two Error lines per
  poll per worker, forever, until someone fixes the row. As specced (DSP-5: head of line by design), but worth a look
  for log volume.
- Under two dispatchers, a batch can come back one short of its size (the 99 above): a row another dispatcher marked
  and committed after this select's snapshot fails PostgreSQL's recheck after the lock, yet still used up a slot of
  the `LIMIT`. The pass then treats the batch as not full and stops its rounds early; the next wake picks up the rest.
  Correct, and exactly once held; just know that "not full" does not mean "empty" under contention.
- An empty batch (0 rows) that commits also resets the size to 100 (`TryBatchAsync` sets it on any success). DSP-5
  says "after a batch of it commits", so this matches; it means a failing outbox whose failing rows disappear resets at
  once, which is what you want.
- `OutboxDispatcher` started its first pass before the probe tables existed and halved every outbox to 25 at boot. Any
  real module gets its table from `migrate` before `serve`, and `StartupChecks` refuses pending migrations, so this
  needs a hand made state; noted only because the halving is per process and survives until the next success.
- The outer `DELETE` of the cleanup used a hash semi join over a sequential scan of the (small, by then) table; only
  the inner select is specced to walk the primary key, and it does. On a large outbox the planner should pick the
  index for the outer side too, but a check against a big table would confirm it.
- The "attempt failed" Warning still formats the next run time with the current culture (`10/21/2026 07:03:33
  +00:00`), as the job queue report noted; deliveries inherit it.
