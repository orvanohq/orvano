# Review, platform data model (feature 8, uncommitted on main), 2026-10-18

**Reviewed by**: Sonnet 5.5 (author model not recorded)
**Scope**: about 45 files (7 tracked, 28 new source and test files, plus lock files), uncommitted on main
**Verdict**: Changes requested
**Follow up (2026-10-18, /debug)**: both Major and all eight Minor findings fixed (one Minor part deferred, see below), three nits fixed, four nits skipped as spec or scope edits. SharedKernel.Tests 85 of 85, ArchitectureTests 20 of 20, CI build and `dotnet format --verify-no-changes` clean. Not yet re-reviewed.

## Summary

You added the shared data conventions in `Orvano.SharedKernel/Data/`: uuid v7 ids, the timestamp interceptor, the convention plugin, text enums, anchors, data classes, the email normalizer, and the `EncryptedValue` mapping. You also added tests on a live PostgreSQL 18 and architecture tests for AC-6 to AC-8. The code is clean and matches the spec closely, and I found no blockers. Two things need fixing before merge. The AC-7 data class check skips owned and complex types, and nothing stops a module from skipping `UseOrvanoConventions` altogether.

## Major

### 🟠 The data class check ignores owned and complex type properties, `server/tests/Orvano.ArchitectureTests/Data/DataModelRules.cs:128`

**Problem**: `ModuleEntityTypes` drops every owned entity type (`!entityType.IsOwned()`), and `DataClasses` only looks at `GetDeclaredProperties()` of the remaining types. Properties of owned types and EF 10 complex types are never checked. AC-7 says "any entity property of type string, string[], byte[], IPAddress, EncryptedValue, or JsonDocument", and CLS-1 says every column.
**Why it matters**: A future `Address` or `Contact` value object holding an email, name or IP would store personal data with no `[DataClass]`, and the test stays green. Feature 49 (finding a user's personal data) and the LOG-3 redaction list both rely on these marks, so the gap would be silent. The same filter also hides owned types from the key and link checks.
**Suggested fix**: Include owned types in the data class walk, and walk complex properties (`GetComplexProperties()` and their nested properties). Add a bad sample context with an owned type and a complex type holding an unmarked string, and assert the rule names it.
**Resolution**: Fixed. `ModuleEntityTypes` keeps owned types, and `DataClasses` walks every declared property plus nested complex properties (`Holder.Contact.Phone.Number`). The key and link checks see owned types too. New bad sample `UnmarkedValueObjectContext` (owned `Address`, nested complex `Contact.Phone`) and a test that names all three unmarked strings; it fails with the old filter.

### 🟠 Nothing enforces that a module uses `UseOrvanoConventions`, `server/src/Orvano.SharedKernel/Data/OrvanoConventions.cs:28` and `server/tests/Orvano.ArchitectureTests/Data/DataModelRules.cs:27`

**Problem**: The plugin only exists when the module calls `UseOrvanoConventions`. A context that calls `UseNpgsql` directly gets PascalCase names, no module schema, a history table in `public`, no `xmin` mapping and no timestamps. `HasTextEnum` then quietly leaves its annotation with no CHECK, and `HasAnchor` maps its stub entity as a real table in the module's own schema, so a migration would try to create it. `ProductionModels()` builds every context but never checks that the options carry the Orvano extension. AC-6 and AC-7 would still pass for such a context, and AC-8 only catches it when it has `Entity` types or a clash on the default schema.
**Why it matters**: The point of this feature is that later modules cannot get the conventions wrong. Today a single forgotten call defeats AC-3 without a failing test.
**Suggested fix**: In `ProductionModels()` (or a new architecture test), fail when a production context's options lack `OrvanoOptionsExtension`, or when its model has no default schema, or when any stub entity is mapped outside the anchor tables. Also fail the model build when a property still carries the `Orvano:TextEnumValues` annotation after finalizing, since that means no CHECK was made.
**Resolution**: Fixed. New public `OrvanoConventions.SchemaOf(IDbContextOptions)`. New rule `DataModelRules.Setup` (run for every production context through `ServerContext.Setup`, new test `Server_contexts_are_set_up_with_UseOrvanoConventions`) fails when the extension is missing, the default schema or history table is not the module's, the context does not retry, an anchor stub maps anything but its anchor table excluded from migrations, or a property still carries `Orvano:TextEnumValues` (no CHECK made). Tests: a plain `UseNpgsql` context with `HasAnchor` and `HasTextEnum` gets all four slips, and a later `UseNpgsql` that moves the history table is caught.

## Minor

### 🟡 Timestamp stamping edits the entity, not the tracked entry, `server/src/Orvano.SharedKernel/Data/TimestampInterceptor.cs:44`

**Problem**: The code sets `entry.Entity.UpdatedAt` directly. With `AutoDetectChangesEnabled = false`, that property change is not marked modified, so `updated_at` may not be written. A caller's changed `CreatedAt` is only excluded from the SQL (`IsModified = false`), so the in-memory entity keeps the caller's wrong value. Changes to an owned entity do not mark the owner `Modified`, so the owner's `updated_at` does not move (COL-1 says editable entities carry it).
**Why it matters**: Edge cases, but they break the "updated_at always moves" promise without any error.
**Suggested fix**: Use `entry.Property(x => x.UpdatedAt).CurrentValue = now` (and the same for `CreatedAt` on add), reset the original value for `CreatedAt` on modify, and decide in a comment or test what happens for owned changes.
**Resolution**: Fixed. The interceptor writes through `entry.Property(...).CurrentValue`, puts the original `CreatedAt` back in memory on modify, and stamps an unchanged owner when an owned entity under it is added, changed, or deleted (found upward through the ownership keys, so a removed owned item counts). Tests: save with `AutoDetectChangesEnabled = false`, an owned only change, the in memory `CreatedAt`, unchanged and deleted entities.

### 🟡 The `npgsql` callback can undo the conventions, `server/src/Orvano.SharedKernel/Data/OrvanoConventions.cs:42`

**Problem**: `npgsql?.Invoke(options)` runs after `MigrationsHistoryTable(...)` and `EnableRetryOnFailure()`, so a module can call `MigrationsHistoryTable` again or `DisableRetryOnFailure`.
**Why it matters**: It is a quiet way around DAT-1 and DAT-8 that the architecture tests would not see.
**Suggested fix**: Invoke the callback first and set the history table and retry after it, or have a test read the history table name and schema from every production context's options.
**Resolution**: Fixed. Retry is enabled before the callback (a module may still tune it), and the history table is set after it, so the callback cannot move it. Test: a callback that moves it to `public.history` leaves `sample.__ef_migrations_history`. The new `Setup` rule also reads the history table and the retry from every production context.

### 🟡 The service provider hash and model cache ignore the schema, `server/src/Orvano.SharedKernel/Data/OrvanoConventions.cs:96`

**Problem**: `GetServiceProviderHashCode` uses only the `TimeProvider` instance. Two instances of one context class with different schemas share a cached model and take the first schema. Separately, every new `TimeProvider` instance builds a new EF internal service provider, which is slow and trips EF's "many service providers" warning (tests make a new `TestClock` per context).
**Why it matters**: Fine for singleton clocks in production, but it is a trap for tests and any multi instance use.
**Suggested fix**: Document that one context class means one schema. Consider holding the clock through a stable wrapper so the hash does not change per instance, or include the schema in the hash.
**Resolution**: Fixed. `UseOrvanoConventions` replaces `IModelCacheKeyFactory` so the cached model keys on the schema too, and the timestamp interceptor is scoped and reads the clock from the context's own options, so the extension hash is constant and every context shares one EF service provider. Tests: one context class in two schemas gets two models; two clocks share one `IModelSource`; a second clock stamps its own time.

### 🟡 CHECK names can pass 63 characters, and colliding enum names are silently merged, `server/src/Orvano.SharedKernel/Data/OrvanoConventionSetPlugin.cs:92` and `server/src/Orvano.SharedKernel/Data/TextEnumMapping.cs:70`

**Problem**: `ck_<table>_<column>` is not length checked, and PostgreSQL silently truncates identifiers at 63 bytes, so two long names could collide or fail to match the name the tests expect. Also, two enum members that map to the same stored text (for example `Oauth` and `OAuth`) overwrite each other in `ByText`, and `Values` uses `Distinct()`, so the clash is hidden.
**Why it matters**: Low odds today, but both fail quietly.
**Suggested fix**: Throw at model build when the CHECK name is over 63 bytes, and throw in `Names<TEnum>` when two members produce the same stored text.
**Resolution**: Fixed. The convention throws at model build when `ck_<table>_<column>` is over 63 UTF-8 bytes. `Names<TEnum>` finds members that store the same text, and `HasTextEnum`, `ToStored`, and the converter throw naming them (`OAuth and Oauth of Provider both store 'oauth'.`). Tests for both.

### 🟡 The architecture tests can pass with nothing to check and do not prove anchors are stubs, `server/tests/Orvano.ArchitectureTests/Data/DataModelRules.cs:27` and `:61`

**Problem**: With no production `DbContext` the AC-6 to AC-8 tests assert over an empty list and pass. They will keep passing if the assembly scan stops finding contexts. `Anchors.Find` matches an anchor only by schema and table name, so a module that maps its own real entity to `platform.project` (with migrations) counts as an anchor target (REF-2 says only the stub, excluded from migrations).
**Why it matters**: A vacuous pass hides a broken scan, and a real table at an anchor name would be created by two modules.
**Suggested fix**: Once the first real context exists, assert the scan found it (a minimum count or named list). In `CrossSchemaLinks`, also require the principal to be an `Anchors.IsStub` type excluded from migrations.
**Resolution**: Partly fixed. `CrossSchemaLinks` now also requires the principal to be an `Anchors.IsStub` type excluded from migrations (new sample `RealAnchorTableContext`, REF-2 message). The scan is split into `ContextTypes`, with a test that it finds `GoodContext`. Skipped for now: a minimum count of production contexts, because the server has none yet (the assertion would fail today); add it with the first module `DbContext`.

### 🟡 The history table is verified for one context and with no real migration, `server/tests/Orvano.SharedKernel.Tests/Data/MigrateHistoryTests.cs:31`

**Problem**: The test ignores `PendingModelChangesWarning` and applies no migration, so it proves that `Migrate()` creates `sample.__ef_migrations_history` and nothing more. It does not check that a first migration containing `EnsureSchema` and a table works after the history table was created, that a row is written to the history table, or that two modules in one database each keep their own table. `SampleDatabase.MigrateAsync` applies differ generated SQL directly and skips the history path.
**Why it matters**: This is the part you flagged as only partly verified live, and it still is. The remaining risk is moderate (Npgsql creates the schema for the history table, which the test shows), but the combined path with a second module is untested.
**Suggested fix**: Add a small hand written `Migration` class to the sample, run `MigrateAsync` end to end, assert a row in `sample.__ef_migrations_history`, and add a second sample context in another schema on the same database to assert two separate history tables and no table in `public`.
**Resolution**: Fixed. `MigrateHistoryTests` now has two module contexts in `first_module` and `second_module` with hand written first migrations (`EnsureSchema` plus a table), runs the real `MigrateAsync` for both on one database, and checks each history table, the `migration_id` row in each, both tables, and nothing in `public`.

### 🟡 Several branches have no test, `server/tests/Orvano.SharedKernel.Tests/Data/`

**Problem**: Not covered: the nullable `HasTextEnum` overload (`TEnum?`) and reading a NULL back, a null `EncryptedValue?` round trip, `EncryptedValue.Comparer` (change detection after mutating bytes in place, snapshot is a copy), an `Unchanged` entity staying unstamped, a `Deleted` entity, an owned type, `SetNull` or `Cascade` for `EndUser`, and `Organization` anchors.
**Why it matters**: These are the branches most likely to break when later features add tables.
**Suggested fix**: Add short tests for each. Most fit the existing `Widget` and `Gadget` sample with one extra property.
**Resolution**: Fixed. Added tests for the nullable `HasTextEnum` (CHECK, nullable column, NULL and a value round trip, stored text), a null `EncryptedValue?` round trip, comparer change detection after mutating bytes in place (snapshot is a copy), unchanged and deleted entities, an owned type, `EndUser` `SetNull` and `Cascade`, and `Organization` `Cascade` (model and live). The sample database now also creates `platform.organization` and `auth.user`.

### 🟡 `EncryptedValue` serializes its bytes as base64, `server/src/Orvano.SharedKernel/Data/EncryptedValue.cs:11`

**Problem**: `ToString` is safe, but `System.Text.Json` (and any structured logger that serializes objects) would write the public `Bytes` property in full. Security baseline LOG-2 and LOG-3 say a secret must never be logged or shown back.
**Why it matters**: The data is ciphertext, so the harm is limited, but the envelope carries the data key id and is still not meant to leave the database.
**Suggested fix**: Add a `JsonConverter` that throws or writes a fixed marker, or mark the type `[JsonIgnore]` on use, and test that a serialized entity does not contain the bytes.
**Resolution**: Fixed. `EncryptedValue` carries a `JsonConverter` that writes the same `EncryptedValue(n bytes)` marker as `ToString` and throws on read. Tests: a serialized `Widget` has the marker and not the base64 bytes; reading throws `JsonException`.

## Nits

- ⚪ `docs/specs/0002-dotnet-backend-practices/index.md:117`, LAY-3 still says `Orvano.SharedKernel` may reference only the shared framework, EF Core relational and Npgsql. It now also references `EFCore.NamingConventions` (spec 0011 build step 1 allows it), so update the sentence and consider an architecture test for allowed packages. **Skipped**: a spec edit, which belongs to `/architect`, not this fix pass.
- ⚪ `docs/specs/0011-platform-data-model/rationale.md:104`, the rows for features 54, 57 and 58 sit above feature 12, so the table is out of order. **Skipped**: a spec edit, for `/architect`.
- ⚪ The scope plan (`docs/scope/foundations.md`) lists "the AC-10 traceability check" under architecture tests, but AC-10 is a hand check in `verify.md`. Either add a small test that every feature number from 12 to 60 appears in the table, or reword the plan line. **Skipped**: a scope edit, for `/scope` or `/sync`.
- ⚪ `server/src/Orvano.SharedKernel/Data/OrvanoConventionSetPlugin.cs:43`, `nameof(Anchors.ProjectAnchor.Id)` is used for all four stubs. It works, but a shared `Id` constant would read better. **Fixed**: `Anchors.StubKey`.
- ⚪ `server/src/Orvano.SharedKernel/Data/ISecretProtector.cs:10`, the contract does not say that `Unprotect` throws on a wrong row, purpose or tampered bytes (never returns empty), that each `Protect` call must use a fresh nonce, or that the data key id in the envelope drives key rotation. Writing this down now helps whoever builds the Infrastructure implementation (ENC-3). **Fixed**: XML docs on both methods.
- ⚪ `server/src/Orvano.SharedKernel/Data/Anchor.cs:46`, the `(Anchor?)` cast in `Find` and `StubAnchor` is clumsy. A `FirstOrDefault` over a small array of tuples would be clearer. **Fixed**: plain loops, no cast.
- ⚪ `docs/specs/0011-platform-data-model/index.md`, status reads "In Progress" while the scope marks build, verify and test as done. Move it to the next state when the review items are closed. **Skipped**: the status moves after a re-review, through `/sync`.

## Strengths

- The conventions are small, spec cited and consistent: one place for the anchor table (`Anchors`) that both `HasAnchor` and the architecture tests read, so the delete rules cannot drift.
- The interceptor is registered through the EF service provider, so it runs ahead of interceptors added with `AddInterceptors`, and a test proves the order. The sync and async paths are both covered, with a fake clock and a caller supplied `CreatedAt` overwritten.
- Tests run against real PostgreSQL 18.6 through the shared assembly fixture, including the CHECK violation by SQL state and constraint name, `NULLS NOT DISTINCT`, partial index, `xmin` concurrency, and the FK delete rules. The migration test proves no `platform.` table and no `xmin` column are created.
- The architecture tests include deliberately bad sample contexts, so each rule is shown to catch its own slip.
- `EmailAddress.Normalize`, `TextEnumMapping` and the `EncryptedValue` converter and comparer are correct as written: NFC, invariant lowercase, NFC again; stored text round trips and throws on an unknown member; null `EncryptedValue?` maps to NULL and the comparer snapshot is a copy. The decomposed versus composed accent test uses really different code points.
- Dependencies are clean: `EFCore.NamingConventions` 10.0.1 is `Apache-2.0` (spec 0009 DEP-1), was published 2026-01-22 (well past the TL-8 three day rule), is only referenced by `Orvano.SharedKernel`, and the lock files resolve the same versions as `Directory.Packages.props`. `SharedKernel` still references no other Orvano project.

## Test coverage

Good for the main paths: ids (AC-1), timestamps on insert, update, sync and async (AC-2), model names, schema, history table option, keys, xmin, CHECK and anchors (AC-3 to AC-5), architecture rules with bad samples (AC-6 to AC-8), email normalization (AC-9), and index forms on PostgreSQL 18 (AC-11). Gaps are listed under the Minor findings: the nullable text enum overload, null and comparer behavior of `EncryptedValue`, unchanged and deleted entities under the interceptor, owned and complex types (also in the rules themselves), a real end to end migration with a second module's history table, and the "no context found" case in the architecture tests. AC-10 is checked by hand only.
