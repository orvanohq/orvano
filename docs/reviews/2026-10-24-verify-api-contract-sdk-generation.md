# /check verify API contract & SDK generation · PASS

_Feature 10, spec [0014](../specs/0014-api-contract-sdk-generation/index.md), all five build milestones, AC-1 to
AC-21. Proof commit `62acb10` on orvano main (orvanohq/orvano#86), 2026-10-24. No `verify.md` exists beside the spec,
so each check comes from the criterion, the rule groups it cites, and the matching *Critical test scenario*. Build
approach: Tracer Bullet (root `AGENTS.md`), so every strand has to be real end to end: server, published image, the
six SDK repos, and the regeneration loop. Local runs used a detached worktree of `62acb10`, fresh clones of each SDK
repo's `main`, Node 25.2.0 with pnpm 12.12.1, .NET 10.0.100, Dart and Flutter stable, Playwright Chromium, and Docker
28.1.1. GitHub evidence came from `gh` against the real repos and Actions runs. Milestone 4 evidence that still holds
is reused from [2026-10-23 regeneration](2026-10-23-verify-api-contract-sdk-generation-regeneration.md) and marked
so._

**PASS: all 21 criteria are met, each one seen running against the server, the published image, the six SDK repos,
or the regeneration loop.** The two items that blocked the milestone 4 verdict are now closed:

1. **RGN-9 fired.** The first scheduled `Regenerate` runs started at 09:36 to 09:39 UTC, about four hours after the
   `23 5 * * *` cron time, and all six succeeded. Each one found `e8a76e2` (orvanohq/orvano#89, which changed tests
   under `api/`) as the newest `api/` commit with an image. The pins on `main` were older, so each run regenerated
   and updated, in place, the one open PR from that morning's dispatch.
2. **`sdk-nextjs` has the new client and server pins.** `sdk-nextjs#14` pins `sdkJsCommit` `7bec6cb` and
   `sdkJsServerCommit` `28ddfe2`, today's `sdk-js` and `sdk-js-server` mains (their `62acb10` regenerate PRs). The
   06:16 dispatch had already picked them up, because those PRs merged at 03:48. The catch up recomputed the same
   pins (`vendor/ matches sdk-js 7bec6cb and sdk-js-server 28ddfe2`). This is what RGN-9 and AC-16 now say, as
   updated by orvanohq/orvano#88.

Proof commit: `62acb10` for the local runs. `main` is now `e8a76e2`, which adds only tests (`PagingTests`,
`PartialUpdateTests`, `WireFormatTests`, generator and stub tests); its CI, CodeQL, and Image runs passed, and all six
regenerate PRs from it are green.

Next: `/test API contract & SDK generation` (the `Test it` box), then a fresh model `/check review`. Six regenerate
PRs from `e8a76e2` are open and green, waiting for you to merge them (RGN-7): `sdk-flutter#9`, `sdk-dart#8`,
`sdk-js#15`, `sdk-js-server#15`, `sdk-nextjs#14`, `sdk-dotnet#14`.

Scope: ticked `Verify it` in `docs/scope/sdks.md`. There is no `verify.md` beside the spec to tick. The spec stays
`In Progress`, because `Test it`, `Review it`, and `Document it` remain (GA workflow).

Known gaps flagged for `/architect` before this run, not counted as new findings: the `sdk-nextjs` pin order (RGN-6),
spec 0007 IMG-3 not naming the `sha-<commit>` tag, and the Flutter drift check comparing only the generated code and
the pin (narrower than RGN-3's `git diff --exit-code`). All three, plus CNF-3's `$absent` matcher and the CNF-8 point
below, are now settled in the specs by orvanohq/orvano#88 (`96dad60`). Out of scope by design: the idempotency replay store
(IDM-4) and the real event envelope (EVS, feature 33).

## Failing

None.

## Blocked

None. Two smaller items from 2026-10-23 stay as they were, and don't block this verdict. The manual bad commit value
was refused only in `sdk-js` (run 40000730903); the same `^[0-9a-f]{40}$` check is in all six `regenerate.yml` files
on `main` today. The App installation's repo list (RGN-4) needs a token with `read:user` (see _For /check review_).

## Evidence by criterion

Shared runs, cited below by name:

- **Server suite**: `dotnet test --solution Orvano.slnx --output Detailed` in `server/`: `total: 1126, failed: 0,
  succeeded: 1126`. Class counts named below come from that log.
- **`pnpm check`** at the repo root: exit 0. It ran Spectral (`No results with a severity of 'warn' or higher
  found!`), `api:check` (`40 scenarios are valid and every public operation is covered.`), the drift check, and the
  strict .NET build (0 warnings, 0 errors). **`pnpm check:lint-fixtures`**: `all 16 restriction rule IDs fire.`
- **`pnpm test`**: exit 0. Tooling 257 of 257, generator 131 passed and 6 skipped (the six live scenarios skip without
  a server), console 30 of 30, server 1126 of 1126.
- **Published image stack**: `ORVANO_IMAGE_TAG=sha-62acb1041f13… docker compose -p verify-0014 -f
  api/conformance/compose.yaml` with ports moved to 28080 and 28081 (another session's stack holds 18080 and 18081).
  The `orvano` container ran `ghcr.io/orvanohq/orvano:sha-62acb1041f13ccd50214ef68a288e9016893970e`. Removed
  afterwards.
- **Local SDK conformance** (fresh clones of each `main`, pointed at that stack): every runtime exit 0, 40 passed, 0
  failed, 0 skipped. Full table under AC-18.

### AC-1 · met

| Check                           | Evidence                                                                                                                                                                                                                                                                               |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Both documents, OpenAPI 3.1     | `api/openapi.json` and `api/console.openapi.json` both say `"openapi":"3.1.1"`, `info.version` `2026-10-22`.                                                                                                                                                                           |
| Emit deterministic, drift fails | `ContractEmitTests` 4 of 4: `Committed_contract_equals_the_emitted_document_when_compared` and `Emit_writes_the_same_bytes_when_run_twice`, each for `public` and `console`. This compare mode is the CI drift check (CTR-5).                                                          |
| Route placement                 | `ContractDocumentTests.Documents_hold_only_their_v1_routes_when_every_role_is_on`. The public document holds only `/v1/health get`; the console document has `paths: {}` today. `ContractTransformerTests` 2 of 2 match the public and console snapshots with sample endpoints mapped. |

### AC-2 · met

| Check                                | Evidence                                                                                                                                                           |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Real documents pass                  | Spectral in `pnpm check` over the public, console, and fixture documents: no findings at `warn` or higher.                                                         |
| Each violation fails naming the rule | `pnpm check:lint-fixtures`: `all 16 restriction rule IDs fire.` The ruleset includes `orvano-catalogued-errors` (XTN-3) and `orvano-paging` (PAG-1, PAG-2, PAG-7). |

### AC-3 · met

| Check | Evidence                                                                                                                                                                                                                                                                  |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Live  | `GET /v1/health/` answers 200 like `/v1/health`; `GET /v1` and `GET /v1/console/nope` answer 404 `not_found` problems, no redirect.                                                                                                                                       |
| Tests | `V1HealthTests.No_v1_endpoint_answers_a_redirect_when_every_role_is_on`; `WireFormatTests.Module_endpoint_answers_under_the_project_group_it_was_given`, `Trailing_slash_matches_the_same_endpoint`, `Project_route_answers_not_found_when_the_project_id_is_not_a_uuid`. |

### AC-4 · met

| Check            | Evidence                                                                                                                                                                                     |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Live, no header  | `GET /v1/health`: `200`, `Orvano-Api-Version: 2026-10-22`.                                                                                                                                   |
| Live, supported  | `Orvano-Api-Version: 2026-10-22`: `200`, same header back.                                                                                                                                   |
| Live, bad values | `1999-01-01` and `banana`: `400`, `application/problem+json`, `"code":"unsupported_api_version"`, `"supportedVersions":["2026-10-22"]`, and the response still carries `Orvano-Api-Version`. |
| Contract         | `info.version` `2026-10-22`; `ContractDocumentTests.Info_carries_the_title_and_current_api_version_and_no_servers` (both documents); `VersioningTypesTests` 19 of 19.                        |

### AC-5 · met

| Check                           | Evidence                                                                                                                                                                                                                                                                                                      |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Live                            | Every response above carries a 32 hex `Orvano-Request-Id` and `Cache-Control: no-store`, errors included. With `traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-…` the server answered `Orvano-Request-Id: 4bf92f3577b34da6a3ce929d0e0e4736`, the trace ID.                                                  |
| CORS lists                      | Live preflight: `Access-Control-Allow-Headers: Authorization,Content-Type,Orvano-Key,Orvano-Api-Version,Orvano-Client,Idempotency-Key,If-Match,traceparent,tracestate`; a browser GET gets `Access-Control-Expose-Headers: Orvano-Request-Id,Orvano-Api-Version,ETag,Retry-After,Orvano-Idempotent-Replayed`. |
| Contract equals `OrvanoHeaders` | `ContractDocumentTests.Header_components_equal_OrvanoHeaders_when_emitted`. `WireFormatTests.Every_v1_response_carries_the_request_id_and_no_store` (4 paths), `Endpoint_with_its_own_cache_control_keeps_it`, `Response_outside_v1_gets_no_orvano_headers`.                                                  |

### AC-6 · met

| Check | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Live  | Unmatched route: 404 `not_found`; `POST /v1/health`: 405 `method_not_allowed`; database down: 503 `unavailable`. Each is `application/problem+json` with `type`, `title`, `status`, `detail`, `code`, `traceId`, and `traceId` equals the response's `Orvano-Request-Id`.                                                                                                                                                                                                                                                                                                                                                                 |
| Tests | `WireFormatTests`: `Handler_error_answers_the_full_problem_shape`, `Validation_failures_answer_field_errors_with_codes`, `Malformed_json_answers_400_invalid_request`, `Unmatched_v1_path_answers_404_not_found_problem`, `Wrong_method_on_a_v1_route_answers_405_method_not_allowed_problem`, `Oversized_body_answers_413_payload_too_large`, `Wrong_content_type_answers_415_unsupported_media_type`, `Unhandled_exception_answers_500_internal_without_the_exception_text`, `Rate_limited_error_answers_retry_after_in_the_body_and_the_header`, `Access_layer_refusal_answers_the_same_problem_shape`. `ErrorProblemsTests` 24 of 24. |

### AC-7 · met

| Check       | Evidence                                                                                                                                                                                 |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Server half | `WireTypesTests` 62 of 62, including `Shared_code_answers_its_catalogued_status` for all 20 codes.                                                                                       |
| Six SDKs    | The 16 kind table stub scenarios (`health.get.status_400` to `status_503`, with 418 and 428 for the fallback rows) passed 16 of 16 in every local runtime of all six SDKs (AC-18 table). |

### AC-8 · met

| Check  | Evidence                                                                                                                                                                                                                                                                                                                                                         |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Server | `WireFormatTests.Response_json_is_camel_case_with_snake_case_enums_utc_timestamps_and_present_nulls`; `Unknown_body_property_answers_validation_failed_with_unknown_field` (3 bodies, including a case variant and a nested object); `Integer_enum_value_answers_invalid_request`; `WireRulesTests` 4 of 4 (every module JSON context refuses unmapped members). |
| SDKs   | `health.get.unknown_fields` and `fixture.get_widget.unknown_enum` passed in every runtime of all six SDKs (all 40 scenarios passed).                                                                                                                                                                                                                             |

### AC-9 · met

| Check  | Evidence                                                                                                                                                                                                                                                                                                                 |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Server | `PartialUpdateTests`: `Patch_changes_nothing_when_the_body_is_empty`, `Patch_clears_a_nullable_field_when_it_is_sent_as_null`, `Patch_fails_required_when_a_non_nullable_field_is_sent_as_null` (`name`, `tags`, `rank`), `Patch_replaces_arrays_and_nested_objects_whole`.                                              |
| SDKs   | `fixture.update_widget.clears_with_null` and `fixture.update_widget.sets_fields` passed by name in every runtime of all six SDKs. Generated shapes: Dart and C# `Optional<T>`, TypeScript `field?: T \| null`, compiled and format checked by `pnpm test:generator:csharp` and `pnpm test:generator:dart` (both exit 0). |

### AC-10 · met

| Check                 | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Server                | `PagingTests` 12 of 12: `List_walks_in_pages_of_the_default_size_when_no_limit_is_sent` (50, 50, 20), `List_order_is_stable_when_rows_are_inserted_between_pages`, `Cursor_fails_invalid_cursor_when_replayed_with_another_filter_or_sort` (3 variants), `Cursor_fails_invalid_cursor_when_it_does_not_parse` (3), `Limit_fails_out_of_range_when_outside_1_to_100` (0 and 101), `Limit_answers_that_many_items_when_at_the_maximum`, `List_returns_the_total_when_the_endpoint_counts`. `PagingTypesTests` 17 of 17. |
| Lint                  | `orvano-paging` fires in `check:lint-fixtures`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| `listAll` in each SDK | `fixture.list_widgets.one_page`, `all_pages`, and `all_cancelled` passed by name in every runtime of all six SDKs.                                                                                                                                                                                                                                                                                                                                                                                                    |

### AC-11 · met

| Check  | Evidence                                                                                                                                                                                                                                                                                                                                                              |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Server | `PartialUpdateTests.Read_sends_a_strong_etag_equal_to_the_version_field`, `Second_write_with_the_same_if_match_fails_version_conflict_with_the_current_version`, `Delete_fails_version_conflict_when_if_match_is_stale`, `Write_proceeds_when_no_if_match_is_sent`, `Write_fails_invalid_request_when_if_match_is_not_one_strong_etag` (`*`, a list, `W/`, unquoted). |
| SDKs   | `fixture.update_widget.if_match` and `fixture.update_widget.version_conflict` passed by name everywhere.                                                                                                                                                                                                                                                              |

### AC-12 · met (replay store out of scope, IDM-4)

| Check               | Evidence                                                                                                                                                                                                                                                                                          |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Server and contract | `IdempotencyKeyTests` 8 of 8 (printable ASCII accepted, over 255, sent twice, and non ASCII refused with `invalid_request`, an unmarked operation ignores the key). The live preflight lists `Idempotency-Key`; the error catalogue lists `idempotency_in_progress` and `idempotency_key_reused`. |
| SDK retry rule      | `fixture.create_widget.no_retry_without_key` and `fixture.create_widget.retry_with_key` passed by name everywhere.                                                                                                                                                                                |

### AC-13 · met

| Check               | Evidence                                                                                                                                                                |
| ------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Live 200            | `GET /v1/health`: `200 {"status":"ok","apiVersions":["2026-10-22"]}`.                                                                                                   |
| Credentials ignored | `Authorization: Bearer not-a-token` and `x-orvano-key: bad_key` both gave the same 200.                                                                                 |
| CORS                | With `Origin: https://app.example`: `Access-Control-Allow-Origin: *` and no `Access-Control-Allow-Credentials`. Preflight: `204`, `Allow-Methods: GET`, `Max-Age: 600`. |
| Not ready           | With the stack's PostgreSQL stopped: `503`, `application/problem+json`, `"code":"unavailable"`, `"detail":"The server is not ready."`.                                  |
| Contract            | `/v1/health get` is `Host_Health` with `x-orvano-sdk` `{service: health, method: get}`, audience `client, server`. `V1HealthTests` 9 of 9.                              |

### AC-14 · met

| Check                         | Evidence                                                                                                                                                                                                                                                                                  |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Five targets, same bytes      | `pnpm test:generator`: 131 passed (the six skips are live scenarios; rerun below). Snapshots exist for `ts-client`, `ts-server`, `dart-client`, `dart-server`, `csharp-server`.                                                                                                           |
| Live scenarios too            | `ORVANO_CONFORMANCE_URL=http://127.0.0.1:28080 vitest run test/conformance.test.ts` in `api/generator`: 80 of 80, no skips.                                                                                                                                                               |
| Passes each language's checks | `pnpm test:generator:csharp` exit 0 (build plus `dotnet format whitespace` and `style --verify-no-changes`); `pnpm test:generator:dart` exit 0 (`All tests passed!`). In the SDK repos the regenerate PRs from `62acb10` passed every analyzer, type, API report, and format job (AC-16). |

### AC-15 · met

| Check                                                | Evidence                                                                                                                                                                                                                                             |
| ---------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Pins today                                           | All six `main` heads (`sdk-flutter b0a2c8b`, `sdk-dart 82269e2`, `sdk-js 7bec6cb`, `sdk-js-server 28ddfe2`, `sdk-nextjs b6ab31e`, `sdk-dotnet b60f5c6`) pin `commit` `62acb1041f13…`, `apiVersion` `2026-10-22`, `contractSha256` `1bb8ae23…844932`. |
| Entry points, drift fails on a hand edit, hash check | Reused from the 2026-10-23 report (one line hand edit, then the entry point, then `git diff --exit-code` exit 1 in all six; a wrong `contractSha256` refused). The workflows and entry points are unchanged since.                                   |
| CI drift job                                         | Green on all six regenerate PRs below and on every `main` CI run.                                                                                                                                                                                    |

### AC-16 · met

| Check                                            | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Dispatch after the signed image                  | Image run 40059509120 (push, `62acb10`, success): `changes` printed `api/ changed: true`; `image` ran `cosign sign --yes "$IMAGE@$DIGEST"` (`Signing artifact...` at 03:45:51) and attested digest `sha256:63d4abaf…4999a09`; `contract-updated` started at 03:46:15, after `image` finished at 03:46:13, and logged `Sent contract-updated 62acb1041f13… to <repo>` for all six.                                                                                                                                                                                                                                  |
| Six PRs, signed off, full CI                     | `sdk-js#14`, `sdk-js-server#14`, `sdk-dart#7`, `sdk-flutter#8`, `sdk-dotnet#12`, `sdk-nextjs#13`: author `app/orvano-sdk-bot`, head `bot/contract-regenerate`, title `chore(sdk): regenerate from contract 62acb10`, one commit each. Every check passed, including `conformance`, `drift`, `hygiene / dco`, `ci-gate`, and CodeQL where present. All six merged by `Atteprojects` between 03:48:48 and 03:50:19.                                                                                                                                                                                                  |
| App, rulesets, workflow shape, bad value refused | Reused from the 2026-10-23 report; `regenerate.yml` is unchanged in all six since then.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `sdk-nextjs` pins in the dispatched PR           | `sdk-nextjs#13` moved `commit` `7067938` to `62acb10`, and `sdkJsCommit` `4a8b31d` to `2535aab`, `sdkJsServerCommit` `2766f26` to `a70cad0`. Those are the `sdk-js` and `sdk-js-server` mains from contract `7067938` (their `#13`, merged at 03:38). The `62acb10` mains (`7bec6cb` from `sdk-js#14`, `28ddfe2` from `sdk-js-server#14`) did not exist yet when the run started at 03:46:22. So this PR is one contract behind on the SDK pins, as the known RGN-6 gap predicts.                                                                                                                                  |
| RGN-9 daily catch up                             | `gh run list -w regenerate.yml --event schedule` in each repo: `sdk-js-server` 40082621948 (09:36:27), `sdk-dotnet` 40082643344 (09:36:46), `sdk-flutter` 40082673330 (09:37:14), `sdk-nextjs` 40082674991 (09:37:15), `sdk-dart` 40082679909 (09:37:20), `sdk-js` 40082792523 (09:39:06), all `schedule`, all success. Each log prints `regenerate: … matches orvano e8a76e2 (contract 2026-10-22).` and then the open PR URL. `sdk-nextjs` prints `vendor/ matches sdk-js 7bec6cb and sdk-js-server 28ddfe2, contract 2026-10-22 at orvano e8a76e2.`                                                             |
| RGN-6, RGN-8 one PR, updated in place            | The 06:16 dispatch for `e8a76e2` (image run 40069380570, `api/ changed: true`, sent to all six) opened `sdk-flutter#9`, `sdk-dart#8`, `sdk-js#15`, `sdk-js-server#15`, `sdk-nextjs#14`, `sdk-dotnet#14`. The catch up then force pushed each one (`head_ref_force_pushed` 09:36:52 to 09:39:30); each still has exactly one commit by the bot, signed off as `orvano-sdk-bot[bot] <346660066+orvano-sdk-bot[bot]@users.noreply.github.com>`. No second PR was opened. After the force push, every check passed on all six, including `conformance`, `drift`, `hygiene / dco`, `ci-gate`, and CodeQL where present. |
| `sdk-nextjs` new pins                            | `sdk-nextjs#14` head `4109dce`: `commit` `e8a76e2…`, `sdkJsCommit` `7bec6cbb2c86…`, `sdkJsServerCommit` `28ddfe236a77…`, which are `sdk-js` and `sdk-js-server` `main` today. `sdk-nextjs` `main` still pins `2535aab` and `a70cad0` until you merge #14.                                                                                                                                                                                                                                                                                                                                                          |
| Workflow shape today                             | `regenerate.yml` changed in five repos at about 21:30 UTC on 2026-10-23, after the earlier report: an empty manual `commit` now runs the catch up. On every `main` today: the `^[0-9a-f]{40}$` check, `DISPATCH_COMMIT` read through `env:`, no `${{ }}` inside any `run:` block, `concurrency: group: regenerate`, cron `23 5 * * *`, and `git commit --quiet --signoff`.                                                                                                                                                                                                                                         |

### AC-17 · met

| Check                                | Evidence                                                                                                                                                                                                                                                                                                                                               |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Scenarios validate, core coverage    | `api:check`: `40 scenarios are valid and every public operation is covered.` With the three live health scenarios deleted from `scenarios/health.yaml` in the worktree, `api:check` failed: `Host_Health (health.get) has no live scenario. Add one, or list it in pending.json with an issue.` Restored, and it passed again. `pending.json` is `[]`. |
| Every scenario runs in every runtime | 40 of 40 in each of the 12 local runtimes (AC-18 table); every `conformance/skip.json` is `[]`. CI on each `main` agrees: `sdk-js` 120 (3 runtimes × 40), `sdk-js-server` 80, `sdk-nextjs` 80, `sdk-dart` 40, `sdk-flutter` 40, `sdk-dotnet` `Total: 40, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0`.                                                |
| A scenario that did not run fails    | In a fresh `sdk-js` clone, with the runner made to leave out `fixture.update_widget.if_match`: exit 1, `Error: 0014 CNF-8: fixture.update_widget.if_match did not run and is not in skip.json.` Restored, clean.                                                                                                                                       |
| Shared stub, per run tokens          | All 12 local runtimes used one stub on 28081 in turn, and the generator suite opens its own run token per scenario (`/__stub/runs/<token>/…`); CI runs Node, Hermes, and Chromium against one stub in one job.                                                                                                                                         |

### AC-18 · met

`health.get.ok` passed by name in every runtime, against the published `sha-62acb10` image:

| Repo (`main`)           | Runtime                                            | Command                                                                                     | Exit   | Passed  |
| ----------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------- | ------ | ------- |
| `sdk-js 7bec6cb`        | Node, Hermes profile, Chromium (Playwright 1.64.1) | `vitest run --project conformance-node` / `-hermes` / `-browser --reporter=verbose`         | 0 each | 40 each |
| `sdk-js-server 28ddfe2` | Node, workerd (Miniflare)                          | `--project conformance-node` / `conformance-workerd`                                        | 0 each | 40 each |
| `sdk-nextjs b6ab31e`    | server entry, client entry                         | `--project conformance-server` / `conformance-client`                                       | 0 each | 40 each |
| `sdk-dart 82269e2`      | `orvano_server`, Dart VM                           | `dart test --tags conformance --reporter expanded`                                          | 0      | 40      |
| `sdk-flutter b0a2c8b`   | `orvano`, `dart test`                              | same, in `packages/orvano`                                                                  | 0      | 40      |
| `sdk-dotnet b60f5c6`    | `Orvano.Server.ConformanceTests`                   | `dotnet run --project tests/Orvano.Server.ConformanceTests -c Release -- -reporter verbose` | 0      | 40      |

### AC-19 · met

| Check                                                          | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Breaking change gets the comment and the label, CI stays green | Probe PR orvanohq/orvano#87 (draft, approved by you for this check), which only removed `apiVersions` from `HealthResponse` in `api/openapi.json`. API diff run 40060843875 succeeded; the PR got the `breaking-api` label and one `github-actions[bot]` comment marked `<!-- orvano-api-diff -->`: `**breaking changes.**`, `error [response-required-property-removed] … in API GET /v1/health removed the required property apiVersions`. Closed unmerged, and `verify/brk-label-probe` deleted (`GET branches/verify/brk-label-probe` answers 404, `git ls-remote` finds nothing). |
| No break, no label                                             | orvanohq/orvano#72: the same marked comment says `no breaking changes.` for both documents, updated in place (created 03:09:47, updated 03:11:36), and no label.                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Never fails                                                    | The workflow's oasdiff steps are `continue-on-error`, and `tooling/scripts/api-diff-report.mjs` exits 0 on a breaking report (seen locally with `PUBLIC_OUTCOME=failure`). `ci-gate` does not wait on `api-diff`.                                                                                                                                                                                                                                                                                                                                                                      |

### AC-20 · met (real envelope out of scope, feature 33)

| Check                          | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `webhooks` keyed by event type | `WebhooksTransformerTests` 4 of 4: `Public_document_has_a_webhook_keyed_by_the_event_type_when_an_event_is_registered`, `Event_schema_lists_every_payload_property_as_required_like_a_response`, `Public_document_has_no_webhooks_when_no_event_is_registered`, `Unregistered_outbox_event_never_appears_in_either_document`. `PublicEventsTests` 5 of 5. The real public document has no `webhooks` today, because no module registers an event yet. |
| Event models emitted           | `WidgetsWidgetCreatedV1Event` is in the `ts-client`, `ts-server`, `dart-client`, `dart-server`, and `csharp-server` snapshots (`Generated/Models/WidgetsWidgetCreatedV1Event.cs` plus its `OrvanoJsonContext` entry).                                                                                                                                                                                                                                 |

### AC-21 · met

| Check                          | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Catalogue                      | `x-orvano-error-codes` in `api/openapi.json` lists 20 codes, each with its status. `ContractDocumentTests.Error_catalogue_lists_every_shared_code_and_operations_list_only_catalogued_codes`, `Console_document_has_no_public_extensions_or_error_catalogue`; `ContractEndpointCheckTests` 7 of 7 (the host refuses to start when `WithErrors` lists a code missing from the catalogue). Spectral `orvano-catalogued-errors` fires in the lint fixtures. |
| `x-orvano-metered`             | The sample snapshot `samples.public.json` shows `"x-orvano-metered": "samples.create"` from `.WithMeter("samples.create")` (`ContractTransformerTests`). No real operation is metered yet.                                                                                                                                                                                                                                                               |
| `OrvanoErrorCodes` in each SDK | `sdk-js` and `sdk-js-server` `src/generated/error-codes.ts`, `sdk-dart` and `sdk-flutter` `lib/src/generated/error_codes.dart`, `sdk-dotnet` `Generated/OrvanoErrorCodes.cs`; the TypeScript one adds `unknown`. `sdk-nextjs` uses the client and server packages' own.                                                                                                                                                                                  |

## For /check review

- CNF-8 reads the same generated run list that the loop iterates. Deleting a scenario from `scenarios.generated.json`
  in `sdk-js` and running the suite exits 0 with 39 passed. CI still catches it, because the conformance job runs
  `pnpm regenerate` first (which rewrote the file) and the drift job runs `git diff --exit-code`. The rule fires when
  the runner leaves a listed scenario out. orvanohq/orvano#88 now says in CNF-8 which CI layer owns the run list.
- While a regenerate PR stays open, the daily catch up force pushes it again even when nothing changed (all six
  `e8a76e2` PRs were rebuilt at 09:37 with the same pins the dispatch wrote at 06:16), so the PR's full CI runs again
  every day until someone merges it. That is harmless, but you may want the catch up to skip an open PR that already
  matches.
- GitHub started the first scheduled runs about four hours after the cron time. RGN-9 only promises a daily run, so
  this is fine, but a lost dispatch can take most of a day to recover.
- The probe PR #87's other jobs were not all finished when it was closed; only `api-diff` was the subject.
- Carried over from the 2026-10-23 report: `sdk-nextjs` conformance imports `src/server.ts` and `src/client.ts`, not
  the built package through its exports map; the App installation's repo list (RGN-4) was not checked; signatures
  were checked through the CI steps, not a local `cosign verify`.
- The JS repos read `ORVANO_LIVE_URL` and `ORVANO_STUB_URL`, while Dart, Flutter, .NET, and the generator read
  `ORVANO_CONFORMANCE_URL` and `ORVANO_CONFORMANCE_STUB_URL`. That works, but one name would be easier to document.
