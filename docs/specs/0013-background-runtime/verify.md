# Verify: background runtime · spec 0013 · updated 2026-10-21

_Steps derived from spec 0013 acceptance criteria, starting with build plan step 31 (outbox version skew). `/check verify` runs these; `/test` locks the durable ones._

## Step 31: outbox version skew

### Commands

- [x] `cd server && dotnet test --solution Orvano.slnx --no-build --filter-trait "Spec=0013:DSP-4"` → passes, including `Rows_of_a_type_this_process_does_not_register_wait_without_holding_back_the_rest_until_a_newer_worker_dispatches_them` → AC-36
- [x] `cd server && dotnet test --solution Orvano.slnx --no-build --filter-trait "Spec=0013:OCL-3"` → passes, including `Cleanup_expires_waiting_events_of_unregistered_types_after_30_days_through_the_pending_index` → AC-39

### Real process probes

- [x] On a `worker` host with the sample outbox, publish registered events and insert waiting rows of a type the host does not register (`sample.note.archived.v1`) before, between, and after them → dispatch marks only the registered rows, the unregistered rows keep `dispatched_at` null with no job, no Warning, no `outbox.dispatched` record for them, and the batch size stays 100 → AC-36 (DSP-3, DSP-4, DSP-5)
- [x] Start a second host whose sample outbox also registers `sample.note.archived.v1` with a subscriber → it dispatches exactly the waiting rows, one job each with key `<event id>:<subscriber>`, and `outbox.lag` for them counts from `occurred_at` (the whole wait) → AC-36, AC-41 (OTL-1)
- [x] On `ManualClock`, waiting rows of an unregistered type 31 days old, one 29 days old, and a registered type's waiting row 31 days old; fire `outbox.cleanup` → only the 31 day old unregistered rows are deleted, one Warning `+ 206` names the outbox and count but no type → AC-39 (OCL-3, OTL-2)
- [x] `EXPLAIN` of OCL-3's inner select → uses `ix_outbox_pending` → AC-39
- [x] Register an outbox with no event types and no table; run a dispatch pass → no select is sent to it and the pass does not fail → AC-36 (DSP-4)
- [x] Log capture across a dispatch with unregistered rows → no event `+ 203` is ever logged (retired) → AC-41 (OTL-2)

### Value sourcing (step 31 rows)

- [x] Dispatch "which rows": vary the host's registered types (old host versus newer host) on the same table → each selects only its own types → DSP-3, DSP-4
- [x] Cleanup "expiry of unregistered types": move the clock so a row sits just under and just over 30 days → deleted only when over, and only by a host that does not register its type → OCL-3

## Acceptance criteria coverage

- AC-36: version skew probes, the no type outbox probe, the DSP-4 trait run
- AC-39: expiry probe, `EXPLAIN`, the OCL-3 trait run
- AC-41: lag of the newer host's dispatch, `+ 203` retired
