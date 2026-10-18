# Verify: platform data model · spec 0011 · updated 2026-10-18

_Steps derived from spec 0011 acceptance criteria and its Value sourcing table. `/check verify` runs these; `/test` locks the durable ones. Run commands from `server/` with Docker running._

## Commands

- [x] `dotnet test --project tests/Orvano.SharedKernel.Tests` → all pass, one `postgres:18.6` container → AC-1 to AC-5, AC-9, AC-11
- [x] `dotnet test --project tests/Orvano.ArchitectureTests` → all pass, including the bad sample contexts → AC-6, AC-7, AC-8
- [x] `dotnet build Orvano.slnx -p:CI=true --no-incremental` and `dotnet format Orvano.slnx --verify-no-changes` → clean → build plan step 1 and 2
- [x] In `Directory.Packages.props`, `EFCore.NamingConventions` is `10.0.1` (Apache 2.0, published 2026-01-22, so older than 3 days) and only `Orvano.SharedKernel` references it → build plan step 1, spec 0009 DEP-1, TL-8

## Behavior checks (what each test must prove)

- [x] A new `Entity` has a v7 `Id` before any save, and the stored row keeps that exact id → AC-1 (`Id_is_set_before_save_and_stored_as_is_when_inserted`)
- [x] 50 ids made 1 ms apart, shuffled, come back from PostgreSQL `ORDER BY id` in time order → AC-1
- [x] With the test clock at 10:00, insert sets both times to 10:00 even when the caller set `CreatedAt` to 1999; with the clock at 10:05, an update sets only `updated_at` and keeps `created_at` at 10:00 → AC-2. Vary the clock, not the machine time: the values must follow the injected `TimeProvider`, never `DateTimeOffset.UtcNow`
- [x] An interceptor added with `AddInterceptors` before `UseOrvanoConventions` already sees the stamped `CreatedAt` → AC-3 (interceptor order)
- [x] The sample model has table `sample.widget` (the entity name, not the DbSet name), snake case columns, `pk_widget`, `fk_widget_project_project_id`, `ix_widget_*` indexes, history table `sample.__ef_migrations_history`, and `Version` on `xmin` of type `xid` → AC-3
- [x] `WidgetKind.VerifyOnly` is stored as `verify_only`, `Es256` as `es256`; `ck_widget_kind` lists exactly `plain`, `verify_only`, `es256`; a raw SQL insert of `VerifyOnly` fails with SQL state `23514` naming `ck_widget_kind` → AC-4
- [x] The generated migration's Up creates only `sample.gadget`, `sample.widget`, and `sample.widget_tag` (the owned tags), with the FK to `platform.project` (Cascade) and `platform.operator` (SetNull), and no SQL creates a `platform.` table or an `xmin` column; Down drops the three sample tables and leaves `platform.project` and `platform.operator` → AC-5
- [x] Deleting the operator sets `created_by_operator_id` to null; deleting the project removes the widget → AC-5
- [x] `HasAnchor(Project, ..., Restrict)`, `HasAnchor(Operator, ..., Cascade)`, and `SetNull` on a `Guid` key each throw `InvalidOperationException` → AC-5
- [x] An FK from schema `bad_link` to `elsewhere.other` fails the cross schema check; a hand made link to `platform.project` with `Restrict` fails it naming REF-3 → AC-6
- [x] An unmarked `string` and `byte[]`, an `EncryptedValue` with no purpose, and one marked `Personal` each fail the data class check → AC-7
- [x] An `int` identity key, an `Entity` key set `ValueGeneratedOnAdd`, an `Entity` keyed on two columns, and two contexts sharing schema `dup` each fail → AC-8
- [x] `EmailAddress.Normalize` lowercases, trims, maps a decomposed `E` plus combining accent and a composed `É` to the same `é`, keeps `+tags`, and throws on null → AC-9
- [x] A second row with the same project and a null `code` hits `ix_gadget_project_id_code` (NULLS NOT DISTINCT); a duplicate `name` is allowed when the first row is revoked and rejected when both are live (`ix_gadget_project_id_name`) → AC-11
- [x] An `EncryptedValue` round trips through `bytea` byte for byte (`Secret` read back equals `[1, 2, 3]`) → Value sourcing, ENC-1
- [x] A context set up with plain `UseNpgsql` fails the `Setup` rule (no `UseOrvanoConventions`, no retry); a later `UseNpgsql` that moves the history table fails it naming DAT-1; an `npgsql` callback that moves the history table still leaves it in `sample` → AC-3 (added 2026-10-18 from the re-review fixes)
- [x] Contexts with different clocks share one EF service provider, and each stamps from its own clock; one context class set up for two schemas gets two models → AC-2, AC-3
- [x] An owned value added, removed, changed two levels down, and an owned collection item added then removed each move the owner's `UpdatedAt` → AC-2
- [x] A nullable enum gets a nullable column and `ck_<table>_<column>`; `HasTextEnum` on a `ToView` entity and inside a complex type each throw at model build → AC-4
- [x] Unmarked text in an owned type and in a nested complex type fails the data class check → AC-7
- [x] An owned collection on EF's default key fails the key check, naming `HasKey(x => x.Id)`; one keyed on `Id` passes → AC-8, IDS-1
- [x] The contexts the scan finds equal `MigrationOrder.DbContexts` (both empty today) → AC-8
- [x] An `EncryptedValue` serializes to JSON as `EncryptedValue(<n> bytes)` with no base64, and reading one from JSON throws → ENC-1

## Records

- [x] `rationale.md` "AC-10 traceability" lists every scope feature from 12 to 60, with "none" where nothing is stored. (54, 57, and 58 were added on 2026-10-18 as "none") → AC-10

## Acceptance criteria coverage

- AC-1: SharedKernel `EntityTests`, `Id_is_set_before_save...`, `Ids_sort_by_their_timestamp...`
- AC-2: `Timestamps_come_from_the_clock...`, `Timestamps_come_from_the_clock_when_saved_without_async` (the sync `SaveChanges` path)
- AC-3: `ConventionModelTests` (names, history table, keys, xmin, CHECK, callback order, model per schema, one service provider), ArchitectureTests `Setup_*`, `Timestamps_are_set_before_another_interceptor_runs...`, `MigrateHistoryTests` (live: `Migrate()` creates `sample.__ef_migrations_history` and no other history table)
- AC-4: `TextEnumMappingTests` (including the throws on a non member value and a null name), `Text_enum_gets_a_named_check...`, `Text_enum_check_rejects_an_unknown_value...`
- AC-5: `Anchor_*`, `Generated_migration_*`, `HasAnchor_throws_*`, `Deleting_the_anchor_project_cascades...`, `Migration_down_drops...`
- AC-6, AC-7, AC-8: ArchitectureTests `DataModelTests` (including `Server_contexts_found_by_the_scan_are_exactly_the_ones_migrate_applies` and the owned collection key test)
- AC-9: `EmailAddressTests`
- AC-10: the Records step above
- AC-11: `Nulls_not_distinct_index...`, `Partial_unique_index...`
- ENC-1: `EncryptedValueTests` (compares by bytes, `ToString` and JSON never print them, JSON read throws), plus the bytea round trip above
- Spec 0002 TST-5: `SingleContainerTests` (one `PostgresFixture` assembly fixture, no test class starts its own container, the class database sits on the shared server)
