# 0004. Dart and Flutter practices for the Orvano Dart SDKs

**Date**: 2026-10-15
**Status**: Accepted
**Updated**: 2026-10-25, reconciled with spec 0014: NET-6 names `Orvano-Api-Version` and `Orvano-Client`; NET-8 names `session_expired`; NET-10's `unknown` is reserved in PRB-8; MDL-4 is JSW-5; MDL-3 partial updates are JSW-9 and JSW-10; PKG-3 templates live in `api/generator/runtime/dart/`; DSC-1 audience tags are XTN-1; the feature 10 follow up is closed.

## Summary

This is the one playbook for writing Orvano's two Dart SDK repos: the Flutter client SDK (two packages, a pure Dart `orvano` and a Flutter layer `orvano_flutter`) and the Dart server SDK (`orvano_server`). They share one toolchain and one hand written runtime, built on package:http, with a cancel token, retries that match the .NET SDK, and a sealed `OrvanoException` tree. The session lives behind a swappable `SessionStore`: the platform keychain through flutter_secure_storage on native platforms, and browser storage on the web. Every package supports Dart 3.10 and later, holds full pub.dev points, and is tested in CI on every platform it claims. Every rule has an ID, so `/audit` can lift it into `AGENTS.md`, tools can enforce it, and reviews can cite it.

## Requirements

This is a cross cutting standard, so it has no build plan of its own. The rules in **Standard definition** are the contract: all code in both Dart SDK repos meets every rule that applies to it, and `/check review` and `/check verify` cite the IDs. Feature 10's generator emits code that meets them, and feature 7 wires the checks. The SDK work inside each service feature (14, 16, 29, 34, and the rest) follows these rules without listing them.

**User stories**:

- As a Flutter developer, I want one `Orvano` object that signs my user in once and keeps them signed in across restarts on iOS, Android, web, and desktop, without me touching the keychain.
- As a Dart backend developer, I want a server SDK that behaves like the .NET one (same errors, retries, and timeouts), runs in AOT executables and Orvano's Dart functions, and refuses to run in a browser or phone app.
- As an app developer, I want to cancel a request when my screen closes, `switch` over every error the SDK can throw, and use my own state management library.
- As an SDK contributor, I want generated and hand written code to follow one layout and one style, checked by the analyzer and CI rather than by memory.
- As a pub.dev visitor, I want full pub points, platform badges that tell the truth, and docs on every public member.

## Decision

**Chosen option**: Option 1: Document the standard and enforce it from the first commit of each Dart SDK repo (analyzer, format, pana score, lower bound dependency test, import and dependency checks, and the conformance suite fail CI; review covers the rest).

Dart SDK code follows the numbered rules below. **MUST** rules fail the build or the review. **SHOULD** rules need a written reason to skip. Rules marked _verify at scaffold_ name a current package API that the first SDK commit must confirm; if it differs, that commit records the working form here.

**Implementation skills**: `dart-run-static-analysis` (`dart-lang/skills`, `.claude/skills/dart-run-static-analysis/`) · `dart-use-pattern-matching` (`dart-lang/skills`, `.claude/skills/dart-use-pattern-matching/`) · `dart-add-unit-test` (`dart-lang/skills`, `.claude/skills/dart-add-unit-test/`) · `dart-resolve-package-conflicts` (`dart-lang/skills`, `.claude/skills/dart-resolve-package-conflicts/`) · `flutter-use-http-package` (`flutter/agent-plugins`, `.claude/skills/flutter-use-http-package/`) · `flutter-add-integration-test` (`flutter/agent-plugins`, `.claude/skills/flutter-add-integration-test/`) · `flutter-add-widget-test` (`flutter/agent-plugins`, `.claude/skills/flutter-add-widget-test/`) · `flutter-implement-json-serialization` (`flutter/agent-plugins`, `.claude/skills/flutter-implement-json-serialization/`) · `dart-modern-features` (`kevmoo/dash_skills`, `.claude/skills/dart-modern-features/`) · `dart-package-maintenance` (`kevmoo/dash_skills`, `.claude/skills/dart-package-maintenance/`)

**This spec wins over any installed skill where they disagree** (rule **DRT-0**). Known conflicts: `dart-add-unit-test` uses mockito with build_runner (use **DQA-1**, hand written fakes). `flutter-implement-json-serialization` teaches json_serializable (use **MDL-1**, plain generated classes). `flutter-use-http-package` and `flutter-implement-json-serialization` move parsing into `compute()` isolates (use **STR-6**: the SDK decodes on the calling isolate). `dart-run-static-analysis` shows `flutter_lints` as a base (use **DRT-4**, `package:lints` plus the Orvano list). `dart-modern-features` covers features up to Dart 3.10, which is exactly the floor (DRT-1); anything newer waits for the floor.

## Standard definition

### Scope and boundaries

Applies to all code in the two Dart SDK repos: the Flutter SDK repo `sdk-flutter` (packages `orvano` and `orvano_flutter`) and the Dart server SDK repo `sdk-dart` (package `orvano_server`), including generated code, tests, examples, and `tool/` scripts. It does not cover the .NET SDK (spec 0002), the TypeScript SDKs (feature 4), or the example apps' own code (feature 60), which follow these rules only where they call the SDK.

Decided elsewhere, not here (this spec points to them and must not be read as deciding them):

| Topic                                                                                                                                                                                                                                                                                                                                                                                                        | Owner                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| Wire format: JSON naming, error body and codes (including the reserved `unknown` code and the session expired code), pagination and cursors, API version header, SDK identification header name, idempotency keys, 64 bit integer wire format, how partial updates mark a field as unset, which endpoints are client only or server only, the generator tool and its templates, the shared conformance suite | Feature 10, API contract & SDK generation                                                                   |
| CORS: allowing Orvano's request headers and exposing `Retry-After` and the trace header to browsers                                                                                                                                                                                                                                                                                                          | Feature 10 (which headers) with the server setup in feature 6                                               |
| What a session contains, token lifetimes and how a refresh works, sign in flows, refreshing across browser tabs and background isolates                                                                                                                                                                                                                                                                      | Feature 14 (and 18, 19 for the other sign in methods)                                                       |
| Token signing, PII classification, dependency vulnerability policy, the final dependency license policy                                                                                                                                                                                                                                                                                                      | Feature 5, Security baseline (dependency licenses: spec 0009 DEP-1, DEP-2)                                  |
| Repo names in the `orvanohq` org; where the server container image is published                                                                                                                                                                                                                                                                                                                              | Feature 6, Stack & architecture (decided in [spec 0007](../0007-stack-architecture/index.md): REP-3, IMG-3) |
| CI wiring and runners, the shared lint config template, PR templates                                                                                                                                                                                                                                                                                                                                         | Feature 7, Coding standards & tooling; feature 58 for repo setup                                            |
| The license text in each package                                                                                                                                                                                                                                                                                                                                                                             | Spec 0009 LIC-1, LIC-6 (Apache 2.0; `LICENSE` and `NOTICE` in each package root)                            |
| pub.dev verified publisher, reserving package names, versions, release steps, changelog process                                                                                                                                                                                                                                                                                                              | Feature 52, SDK publishing & versioning                                                                     |
| Generated row types, `orvano types`, whether system fields sit inside a row type                                                                                                                                                                                                                                                                                                                             | Feature 56, Type generation                                                                                 |
| Realtime protocol, reconnect rules, the WebSocket package                                                                                                                                                                                                                                                                                                                                                    | Features 33, 34, 35                                                                                         |
| Upload transport, progress, resumable uploads (they use NET-9's streamed primitive)                                                                                                                                                                                                                                                                                                                          | Features 29 and 30                                                                                          |
| Push device registration                                                                                                                                                                                                                                                                                                                                                                                     | Feature 44                                                                                                  |
| OAuth deep link callbacks                                                                                                                                                                                                                                                                                                                                                                                    | Deferred item, OAuth social sign in                                                                         |

### General rules: toolchain and language

- **DRT-0 (MUST)**: Where an installed agent skill, a tutorial, or generated scaffold disagrees with this spec, this spec wins. A deviation needs a comment `// Deviation <RULE-ID>: <reason>` at the spot and a mention in the PR.
- **DRT-1 (MUST)**: SDK floor: `environment: sdk: ^3.10.0` in every package, and `flutter: ">=3.38.0"` in `orvano_flutter` (Flutter 3.38 shipped Dart 3.10 in November 2025). The floor sits about one year back: each November, after the new Flutter stable ships, set it to the Dart version of the previous November's stable (November 2026 keeps 3.10). Code uses only language features the floor allows; primary constructors (Dart 3.13) wait. CI tests the floor and the latest stable (today Dart 3.13 and Flutter 3.47). A pub workspace that contains a Flutter package resolves only with the Flutter tool, so the Flutter SDK repo's CI always runs on a Flutter SDK and its bundled Dart (the floor job uses Flutter 3.38 and its Dart 3.10), including `dart test` inside `packages/orvano`. The server SDK repo uses the plain Dart SDK. Apps still use `orvano` without Flutter, since the published package has no Flutter dependency.
- **DRT-2 (MUST)**: Runtime dependencies are exactly these. Any other needs a spec update.
  - `orvano`: `http`, `meta`, `clock`, `logging`, `collection` (deep equality in models, MDL-1), `web` (used only behind the web conditional import, PLT-1).
  - `orvano_flutter`: `orvano`, `flutter`, `flutter_secure_storage` (version 11), `shared_preferences` (the first run marker only, SES-7).
  - `orvano_server`: `http`, `meta`, `clock`, `logging`, `collection`.
    Each one also meets spec 0002 GEN-5 (a release or commit in the last 12 months), and its license is on spec 0009 DEP-1's Permissive list (DEP-2).
- **DRT-3 (MUST)**: Dependency constraints are caret ranges starting at the lowest version that works (`http: ^1.5.0`, since 1.5.0 added aborting requests), never forcing apps onto the newest. The one exception is PKG-4's range for `orvano`. CI runs a downgrade (`flutter pub downgrade` in the Flutter repo, `dart pub downgrade` in the server repo) and then analyze plus tests, so the lower bounds are real; a second job runs on the newest versions. No `dependency_overrides` in any published package or committed pubspec. The workspace `pubspec.lock` is committed, so CI is reproducible. Some newest releases already need more than the floor (as of 2026-10-15, `test` 1.33 needs Dart 3.11, and `shared_preferences` 2.5.6 needs Dart 3.11 and Flutter 3.41), so lower bounds must stay low enough that the floor job resolves an older release (for example `test: ^1.25.0`, `shared_preferences: ^2.3.0`); never raise a lower bound past what the floor can resolve. Tools that only run in CI and need a newer SDK (pana) run on the latest stable job only.
- **DRT-4 (MUST)**: One shared `analysis_options.yaml` per repo: `include: package:lints/recommended.yaml`; `analyzer: language:` with `strict-casts`, `strict-inference`, and `strict-raw-types` all true; generated files are not excluded. Extra lint rules on top: `public_member_api_docs`, `comment_references`, `unawaited_futures`, `discarded_futures`, `avoid_dynamic_calls`, `cancel_subscriptions`, `close_sinks`, `only_throw_errors`, `avoid_catches_without_on_clauses`, `avoid_print`, `always_declare_return_types`, `type_annotate_public_apis`, `prefer_final_fields`, `prefer_const_constructors`, `use_super_parameters`, `directives_ordering`, `no_runtimeType_toString`, `cast_nullable_to_non_nullable`, `invalid_runtime_check_with_js_interop_types`, `deprecated_member_use_from_same_package`, `implementation_imports`. `example/` and `tool/` each have their own `analysis_options.yaml` that includes the repo's file and turns off `avoid_print` and `public_member_api_docs`. CI runs `dart analyze --fatal-infos` (`flutter analyze --fatal-infos` in the Flutter repo). All of these names were confirmed current on dart.dev on 2026-10-15, none deprecated; _verify at scaffold_ only the casing of `no_runtimeType_toString` (dart.dev lists it in lowercase), and that each rule exists in the floor's analyzer (drop any that don't with a note here).
- **DRT-5 (MUST)**: `dart format` with its defaults (the tall style, width 80) on all code, generated code included. CI runs `dart format --output=none --set-exit-if-changed .`.
- **DRT-6 (MUST)**: Null safety and types. No `dynamic` in code you write; JSON is `Map<String, Object?>` and `List<Object?>`, read with patterns or checked casts (MDL-2). The `!` operator is allowed outside tests only with a `// Not null: <why>` comment on the line. In SDK code, `late` only for a field set in the constructor body; never for lazy public state. Programming errors throw `ArgumentError`, `StateError`, or `UnsupportedError` (Dart `Error`s); every runtime failure is an `OrvanoException` (EXC-1).
- **DRT-7 (MUST)**: Class modifiers make the public API deliberate: a public class is `final` unless it is meant to be implemented (`abstract interface class`, for example `SessionStore`) or is a closed family (`sealed`, for example `OrvanoException`, `AuthState`). A `final` class can gain members without breaking apps. An interface or a sealed family cannot: adding an abstract member to an interface, or a subtype to a sealed family, breaks app code and needs a major version after the first beta. So the five `OrvanoException` subtypes (EXC-1) are fixed, and feature 14 settles every `AuthState` subtype before 1.0.
- **DRT-8 (MUST)**: Each package has one public library, `lib/<package>.dart`, which exports from `lib/src/` with explicit `show` lists, so nothing becomes public by accident. The one exception: `orvano_flutter` exports all of `package:orvano/orvano.dart` (PKG-4). No `part` files. File names are `snake_case`. No package imports another package's `src/` (`implementation_imports`, part of the recommended set).

### Package layout

- **PKG-1 (MUST)**: The Flutter SDK repo `sdk-flutter` is a pub workspace (repo names per spec 0007 REP-3):

```
sdk-flutter/
  pubspec.yaml               # workspace root: publish_to: none, sdk ^3.10.0,
                             #   workspace: [packages/orvano, packages/orvano_flutter, packages/orvano_flutter/example]
  pubspec.lock               # committed (DRT-3)
  analysis_options.yaml      # shared (DRT-4)
  tool/                      # Dart scripts: check_imports.dart, check_deps.dart, check_version.dart, check_pana.dart, test_all.dart
  packages/
    orvano/                  # pure Dart client: VM, web (js and wasm), Flutter
      pubspec.yaml           # resolution: workspace
      dart_test.yaml         # declares the conformance tag (DQA-3)
      lib/orvano.dart        # the one public library (DRT-8)
      lib/src/generated/     # feature 10 output, never edited by hand (MDL-1)
      lib/src/core/          # hand written runtime: transport, auth hook, retry, cancel token, options, exceptions, codecs (from feature 10's shared templates, PKG-3)
      lib/src/session/       # SessionStore, MemorySessionStore, BrowserSessionStore (behind the web conditional import)
      lib/src/services/      # hand written layers: auth, databases table handles, paging
      lib/src/version.dart   # package version constant, checked against pubspec (tool/check_version.dart)
      test/                  # unit tests (VM and Chrome); test/conformance/ tagged conformance
      example/main.dart
      README.md CHANGELOG.md LICENSE
    orvano_flutter/          # Flutter layer: secure store, app lifecycle, exports all of orvano
      pubspec.yaml           # resolution: workspace
      lib/orvano_flutter.dart
      lib/src/secure_session_store.dart
      lib/src/first_run.dart
      lib/src/lifecycle.dart
      lib/src/orvano_flutter.dart   # OrvanoFlutter.open
      test/
      example/               # a small Flutter app (a workspace member, so it uses the local orvano);
                             #   example/integration_test/ holds the device tests (DQA-4)
      README.md CHANGELOG.md LICENSE
```

- **PKG-2 (MUST)**: The Dart server SDK repo `sdk-dart` is a single package, laid out like `packages/orvano` above but at the repo root: `pubspec.yaml` (package `orvano_server`, PLT-3), `dart_test.yaml`, `lib/orvano_server.dart`, `lib/src/generated/`, `lib/src/core/`, `lib/src/services/`, `lib/src/version.dart`, `test/` with `test/conformance/`, `example/main.dart`, `tool/`, and the same root files. It has no `session/` folder: the server SDK authenticates with an API key only.
- **PKG-3 (MUST)**: The hand written runtime in `lib/src/core/` (transport, auth hook, retry, cancel token, options, exceptions, codecs, the paging helper) comes from one template set in `api/generator/runtime/dart/` (spec 0014 GNR-5) and is emitted into both repos. Neither repo depends on the other. A change to the runtime is made in the templates and regenerated into both; the conformance suite (DQA-3) proves they behave the same.
- **PKG-4 (MUST)**: `orvano_flutter` exports all of `package:orvano/orvano.dart`, so a Flutter app imports only `package:orvano_flutter/orvano_flutter.dart`. Both packages release at the same version (feature 52 owns how), and `orvano_flutter` depends on `orvano` with an explicit range on the same minor version, `orvano: ">=X.Y.Z <X.(Y+1).0"` (a caret would allow the next minor after 1.0).
- **PKG-5 (MUST)**: No Orvano package ships native code (no `android/`, `ios/`, `macos/`, `windows/`, `linux/` plugin folders). Platform work goes through existing federated plugins (flutter_secure_storage, shared_preferences). This keeps five native languages out of the SDK team's review load and avoids the pana cap on legacy native builds.
- **PKG-6 (MUST)**: The workspace runs on Dart's built in pub workspaces plus small Dart scripts in `tool/` (for example `dart run tool/test_all.dart` runs every package's tests). No melos. Revisit if the package count grows past three.

### Client API shape

- **CLN-1 (MUST)**: An app gets a client by opening an explicit instance; there is no global singleton and no `Orvano.instance`.
  - Pure Dart: `final orvano = await Orvano.open(endpoint: Uri.parse('https://...'), projectId: '...', sessionStore: ..., httpClient: ..., lifecycle: ..., options: ...)`. Only `endpoint` and `projectId` are required. `open` reads the stored session (no network I/O; a token near expiry is refreshed on first use, per feature 14) so `orvano.auth.currentState` is correct from the first frame.
  - Flutter: `final orvano = await OrvanoFlutter.open(endpoint: ..., projectId: ..., observeLifecycle: true)` returns the same `Orvano` type. It calls `WidgetsFlutterBinding.ensureInitialized()` itself and wires `SecureSessionStore` (or `BrowserSessionStore` on the web), the first run cleanup (SES-7), and the app lifecycle source (CLN-6). A background isolate passes `observeLifecycle: false`. Tests may pass their own `sessionStore`.
  - Server: `final orvano = OrvanoServer(endpoint: ..., projectId: ..., apiKey: ..., httpClient: ..., options: ...)`. It is synchronous, since there is no session to restore.
  - `open` and `OrvanoServer` throw `ArgumentError` when the endpoint is not absolute, carries userinfo, a query, or a fragment, breaks NET-7, or when `projectId` (or `apiKey`) is empty. `OrvanoServer` throws `UnsupportedError` on Android and iOS (PLT-3). A failing session store never makes `open` throw (SES-5).
  - The endpoint may carry a base path (`https://example.com/orvano`). It is normalized to end in `/`, and every request path is relative to it.
  - Open one client per project and endpoint. Two clients with the same project and endpoint share a session key (SES-2) and don't see each other's changes in the same tab or process.
- **CLN-2 (MUST)**: Services hang off the client as getters that always return the same object: `orvano.auth`, `orvano.databases`, `orvano.storage`, and so on as features add them. Service and method names come from feature 10's contract, written in Dart style (`lowerCamelCase`, no `Async` suffix, no `get` prefix on getters).
- **CLN-3 (MUST)**: Every I/O method is `async` and returns a `Future`, so argument and state errors arrive as Future errors, never as synchronous throws. Inputs are named parameters (`required` where the contract requires them; one positional parameter only when it is the obvious subject, such as a table name), ending with an optional named `OrvanoRequestOptions? options`. No synchronous wrappers. JSON shaped inputs are typed models, never `Map` literals, except `OrvanoRow` data.
- **CLN-4 (MUST)**: Paged reads return `Future<ResultPage<T>>` (items plus the cursor feature 10 defines) and are paired with a `Stream<T> listAll(...)` that fetches pages lazily as the listener consumes them and stops fetching when the subscription is cancelled. (Names avoid `Row` and `Page`, which clash with Flutter's `Row` widget and `Page` route.)
- **CLN-5 (MUST)**: `Future<void> close()` on every client, safe to call more than once. In order: it marks the client closed; aborts every call in flight (each gets `OrvanoCancelledException` with reason `OrvanoCancelReason.clientClosed`); awaits pending session store writes (SES-9); closes every stream the client created (STR-4); and closes the `http.Client` only if the SDK created it (NET-1). Any call made after `close()` fails with `StateError`; listening to a client stream after `close()` gives a stream that is done at once.
- **CLN-6 (MUST)**: The app lifecycle reaches the core through an `AppLifecycleSource` interface (`AppPhase get current` plus `Stream<AppPhase> get changes`, with `AppPhase.resumed` and `AppPhase.paused`) passed to `Orvano.open` as `lifecycle:`. Pure Dart has none (always resumed). `orvano_flutter` feeds it from Flutter's `AppLifecycleListener`: `resumed` maps to `resumed`; `hidden`, `paused`, and `detached` map to `paused`; `inactive` is ignored (it flickers on iOS for overlays). The starting phase comes from `WidgetsBinding.instance.lifecycleState`, or `resumed` when that is null. What the SDK does on resume (retry a failed store, SES-5; refresh a session near expiry, feature 14; reconnect realtime, feature 34) is listed with each feature; this rule fixes only where the signal comes from.
- **CLN-7 (MUST)**: The SDK is state management agnostic: it exposes `Future`s, `Stream`s, and synchronous current values only, and never depends on Provider, Riverpod, Bloc, GetX, signals, or rxdart. The README shows short recipes for Riverpod and Bloc that wrap `orvano.auth.stateChanges`.
- **CLN-8 (MUST)**: Options are immutable `final class` values with these fields and defaults:
  - `OrvanoClientOptions`: `timeout` (30 s), `retry` (`RetryOptions()`), `defaultHeaders` (empty), `allowInsecureEndpoint` (false).
  - `RetryOptions`: `maxRetries` (2), `baseDelay` (500 ms), `maxRetryAfter` (30 s).
  - `OrvanoRequestOptions`: `timeout`, `retry`, `cancelToken`, `headers`, all nullable; a null field falls back to the client's value. Per call headers override default headers, and neither may set the auth, API version, or SDK identification headers (`ArgumentError`).

### Async and streams

- **STR-1 (MUST)**: Every `Future` is awaited, returned, or passed to `unawaited(...)` with a comment saying why (`unawaited_futures`, `discarded_futures`). No `Completer` leaves the function or object that owns it.
- **STR-2 (MUST)**: Auth state is `Stream<AuthState> get stateChanges` on `orvano.auth`, a broadcast stream that gives each new listener the current state first and then every change, plus `AuthState get currentState` for synchronous reads. It is built on a small internal `StateStream<T>` helper using `Stream.multi(..., isBroadcast: true)` (a plain broadcast controller can't send the current value to only the new listener), not rxdart. `AuthState` is `sealed`: `AuthSignedOut` and `AuthSignedIn(session, user)` (their payloads come from feature 14). Every state also carries `persistence` (`SessionPersistence.persistent` or `SessionPersistence.memoryOnly`) and, when memory only, `storeFailure`, a `SessionStoreFailure` (an exported `final class` with `message` and `cause`; SES-5). A new state is emitted after the store write it causes has finished or failed, and only on sign in, sign out, a change of user, or a change of persistence. A token refresh updates `currentState` silently, so `StreamBuilder`s don't rebuild on every refresh.
- **STR-3 (MUST)**: A subscription (realtime, feature 34) is a single subscription `Stream<T>` that opens its server subscription on `listen`, closes it on `cancel`, and delivers recoverable problems as `OrvanoException` error events without closing. Reconnecting is internal. The protocol and reconnect timing belong to features 33 and 34.
- **STR-4 (MUST)**: Every `StreamController` the SDK creates is closed by `close()` (CLN-5) or by its own `onCancel`. Every internal `StreamSubscription` is cancelled (`cancel_subscriptions`, `close_sinks`).
- **STR-5 (MUST)**: Time comes from `clock` (package:clock), including request durations (`clock.stopwatch()`), and delays from `Timer` or `Future.delayed`, so tests control both with `fake_async` (DQA-1). Retry jitter uses a `Random` the transport receives (a seeded one in tests). No `DateTime.now()` in `lib/`.
- **STR-6 (MUST)**: The SDK decodes JSON on the calling isolate and never spawns isolates or calls `compute()`. A client instance belongs to the isolate that opened it; a background isolate (for example a background task) opens its own. Revisit only if profiling the largest page size feature 10 allows shows dropped frames.

### Networking

- **NET-1 (MUST)**: All HTTP goes through one `http.Client` from package:http. An app may pass its own (`httpClient:`), for example `cupertino_http`, `cronet_http`, or a client with certificate pinning; the SDK never closes a client it did not create. Without one, the SDK creates `http.Client()` (the platform default: dart:io on native, the browser client on the web) and closes it in `close()`. The SDK never depends on a native client package; the README shows how to plug one in, and says aborting needs a client that supports package:http's abortable requests (NET-2).
- **NET-2 (MUST)**: Requests are sent as abortable requests, so a timeout, a cancel, or `close()` closes the connection: `AbortableRequest(method, url, abortTrigger: ...)` for buffered calls and `AbortableStreamedRequest` for NET-9, both built on package:http's `Abortable` mixin (package:http 1.5.0 or later; confirmed in its source on 2026-10-15). An abort surfaces as `RequestAbortedException`, which extends `ClientException`, so the transport checks for it before mapping other client errors. package:http documents that some clients don't support aborting, and `MockClient` doesn't, so the transport also races the send (and the body read) against the same trigger: the caller is released on time even with a client that ignores aborting (`MockClient`, an older native client, or a wrapper that rebuilds requests); such a connection may run on in the background. Never use `Future.timeout` around a request. Each attempt builds a fresh request from a request factory.
- **NET-3 (MUST)**: Built in retry, matching spec 0002 LIB-7: only GET, HEAD, OPTIONS, and requests carrying an idempotency key (if feature 10 adds them); on 408, 429, 502, 503, and 504, on attempt timeouts, and on network errors other than TLS and certificate failures (those never retry); at most `maxRetries` retries (2) with exponential backoff and full jitter from `baseDelay` (0.5 s). A `Retry-After` header (in seconds or as an HTTP date) replaces the backoff for that attempt when it is at most `maxRetryAfter` (30 s); when it is longer, the SDK stops and throws (an `OrvanoRateLimitedException` on 429) so the app decides. `RetryOptions(maxRetries: 0)` turns retry off. A cancel or `close()` during a backoff wait ends the wait at once. Streamed request bodies are retried only when they can be replayed (NET-9). There is no total deadline beyond the per attempt timeout, the same as .NET; the README states the worst case (3 attempts of up to 30 s plus waits). No `RetryClient` from package:http (it retries by status alone and can't see the cancel token).
- **NET-4 (MUST)**: Default timeout 30 s per attempt, covering the response headers and the whole buffered body (for streamed responses, the headers only; NET-9), set in `OrvanoClientOptions.timeout` and overridable per call. When the last attempt times out, the SDK throws `OrvanoTimeoutException`.
- **NET-5 (MUST)**: Cancelling uses `OrvanoCancelToken`: `cancel([Object? reason])`, `isCancelled`, `reason`, and `whenCancelled`. One token may be passed to many calls. The transport registers a callback with the token for each attempt and removes it when the attempt ends, so a long lived token doesn't collect listeners. A call given an already cancelled token throws `OrvanoCancelledException` before sending; a call cancelled in flight aborts the request and throws `OrvanoCancelledException`.
- **NET-6 (MUST)**: Every request sends `Orvano-Api-Version` and the SDK identification header `Orvano-Client` (spec 0014 HDR-1, HDR-3), the latter with the value `<package>/<version>`: `orvano_flutter/<version>` for a client opened through `OrvanoFlutter.open`, `orvano/<version>` otherwise, `orvano_server/<version>` for the server SDK, from each package's `lib/src/version.dart`. The SDK sets `User-Agent` only on native platforms (browsers forbid it), chosen through the same conditional import as PLT-1.
- **NET-7 (MUST)**: The endpoint must be `https`, except for the hosts `localhost`, `127.0.0.1`, `::1`, and `*.localhost`, or when the app sets `OrvanoClientOptions(allowInsecureEndpoint: true)` (a self hosted server on a home network, or the Android emulator's `10.0.2.2`). Otherwise `open` throws `ArgumentError`. The README notes that Android and iOS also block plain HTTP unless the app allows it.
- **NET-8 (MUST)**: Auth reaches each attempt through an internal `AuthHeaderProvider` in the core: `Future<Map<String, String>> headers()`, called fresh for every attempt (so a refreshed token is used on retries), and `Future<bool> onUnauthorized(OrvanoApiException error)`. The client SDK's provider returns the session's header, and on a 401 carrying the session expired code `session_expired` (spec 0014 PRB-8) it refreshes the session once (feature 14 defines how) and returns true; the transport then retries that request once, whatever its method, since the server rejected it before acting. A second 401, or a failed refresh, signs the user out (STR-2) and throws the `OrvanoApiException`. The server SDK's provider returns the API key header and never retries a 401.
- **NET-9 (MUST)**: The transport has one streamed primitive (a request with a streamed body in, `http.StreamedResponse` out, timeout on the headers only, cancel and close still abort it), and the buffered call that ordinary methods use is built on it. Uploads (features 29 and 30), server sent events, and streamed function output (features 33 and 37) use the streamed primitive. A streamed body is retried only if the caller supplied a way to replay it.
- **NET-10 (MUST)**: Response handling: bodies are decoded as UTF 8 from `bodyBytes` (never `response.body`, which falls back to Latin 1 without a charset). The SDK sets `followRedirects = false` on every request, so a redirect is never followed silently. On native platforms the 3xx becomes an `OrvanoApiException`. package:http's browser client turns the same setting into a fetch that refuses redirects, and the browser hides the 3xx, so on the web it surfaces as an `OrvanoNetworkException`. An error response whose body isn't the contract's error JSON (an empty body, or a proxy's HTML page) becomes an `OrvanoApiException` with `code: OrvanoErrorCodes.unknown` (reserved in spec 0014 PRB-8, KND-3), the HTTP reason phrase as `title`, and `rawBody` cut to 4 KB.

### Errors

- **EXC-1 (MUST)**: Every runtime failure the SDK reports is an `OrvanoException`, a `sealed class` with exactly these subtypes, so apps can `switch` exhaustively (programming errors are Dart `Error`s instead, DRT-6):
  - `OrvanoApiException`: the server answered with an error. Carries `statusCode`, `code`, `title`, `detail`, `traceId`, and `rawBody` (the same fields as spec 0002 LIB-6). Subtypes only when a case carries extra data: `OrvanoRateLimitedException` (status 429 only) with `retryAfter`.
  - `OrvanoNetworkException`: no usable response (connection refused, DNS failure, TLS failure, connection dropped). Carries `cause`.
  - `OrvanoTimeoutException`: the last attempt ran out of time. Carries `timeout`.
  - `OrvanoCancelledException`: the call was cancelled, by its token or by `close()`. Carries `reason` (the token's reason, or `OrvanoCancelReason.clientClosed`).
  - `OrvanoProtocolException`: the server's reply could not be read (bad JSON, a missing required field, an unexpected type). Carries the JSON `path` and the cause, but never the JSON values (EXC-5).
- **EXC-2 (MUST)**: No raw `ClientException`, `SocketException`, `HandshakeException`, `TimeoutException`, `FormatException`, or `TypeError` escapes the SDK. The transport catches `Object` (after rethrowing its own `OrvanoException`s) and maps it, keeping the original stack trace (`Error.throwWithStackTrace`).
- **EXC-3 (MUST)**: Error codes are `static const String` members of `OrvanoErrorCodes`, generated from the contract. An unknown code still produces an `OrvanoApiException`; apps match codes with constant patterns (`OrvanoApiException(code: OrvanoErrorCodes.notFound)`).
- **EXC-4 (MUST)**: Bad arguments fail with `ArgumentError` before any request is sent; using a closed client fails with `StateError` (DRT-6, CLN-3).
- **EXC-5 (MUST)**: `toString()` of any exception shows its type, status, code, and trace ID, never a token, key, request body, or response value (DSC-2).

### Models and codecs

- **MDL-1 (MUST)**: Models (generated by feature 10, or hand written in services) are immutable `final class` types with `final` fields, a `const` constructor where possible, `factory X.fromJson(Map<String, Object?> json)`, `Map<String, Object?> toJson()`, `copyWith`, value equality (`==` and `hashCode`, using package:collection's deep equality for list and map fields), and a `toString` that redacts secrets. `copyWith` can't set a field to null (use the constructor), and the docs say so. Generated code lives in `lib/src/generated/`, starts with `// GENERATED CODE - DO NOT MODIFY BY HAND`, is committed, is never edited by hand, and meets every rule here except naming style it inherits from the contract. It is documented from the contract's descriptions. App developers never run build_runner because of Orvano; the SDK packages have no build_runner, json_serializable, or freezed dependency.
- **MDL-2 (MUST)**: `fromJson` checks fields one at a time and reports the first bad one by its path (`$.title`, `$.items[3].id`): a required field that is missing or has the wrong type, or an optional field with the wrong type, throws `OrvanoProtocolException` with that path and the expected type, never a `TypeError` and never the value. Unknown fields are ignored, so newer servers work with older SDKs. Numbers are read as `num` and converted (`toDouble()`, or `toInt()` after checking the value is whole), because the web reads JSON `5` as a JavaScript number, unlike the VM.
- **MDL-3 (MUST)**: Contract enums are Dart enhanced enums with a `wireName` and an extra `unknown` value whose `wireName` is null. Reading an unknown value gives `unknown` instead of throwing. Sending `unknown` throws `ArgumentError`. Response models and input models are separate types, and partial update inputs mark a field as unset with `Optional<T>` (`Optional.absent()` by default, sent as an omitted key; spec 0014 JSW-9, JSW-10), so a read value is never written back unchanged.
- **MDL-4 (MUST)**: Timestamps are parsed into UTC `DateTime` (a timestamp without an offset is read as UTC, not local time) and sent as `toUtc().toIso8601String()`. A 64 bit integer that may exceed 2^53 is never a Dart `int` (web builds store `int` as a JavaScript number and lose precision); it follows spec 0014 JSW-5: a value that always fits in 2^53 − 1 is a JSON number read as `int`, and one that can exceed it is a JSON string, read as a `String`.
- **MDL-5 (MUST)**: Typed rows plug in through `OrvanoCodec<T>`, an `abstract interface class` with `T fromJson(Map<String, Object?> json)` and `Map<String, Object?> toJson(T value)`, plus a `OrvanoCodec.of(fromJson: ..., toJson: ...)` factory. `orvano.databases.table('todos')` returns a `TableHandle<OrvanoRow>`, where `OrvanoRow` holds a `Map<String, Object?>`. `.withCodec(Todo.codec)` on it returns a `TableHandle<Todo>` whose reads give `Todo` and whose writes take `Todo`. (Two steps rather than a type argument on `table`, which would allow `table<Todo>('todos')` with no codec.) Feature 56's generated classes expose a static `codec`; whether system fields (ID, timestamps) sit inside `T` or beside it is feature 56's call, and the codec signature stays the same either way. How a table is addressed (database and table IDs) comes from features 15 and 16.

### Session storage

- **SES-1 (MUST)**: The session lives behind one interface in `orvano`:

```dart
abstract interface class SessionStore {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> delete(String key);

  /// Keys starting with [prefix], used by the first run cleanup.
  Future<List<String>> keys(String prefix);

  /// A broadcast stream of changes to [key] made outside this client (another
  /// tab), where null means deleted; or null when the store can't see them.
  Stream<String?>? watch(String key);
}
```

The SDK stores one JSON string per client: the session as feature 14 defines it, plus a schema version field `v`. Apps may pass their own store (an encrypted database, a test fake). A store may throw anything; the SDK treats any error as a store failure (SES-5). The client cancels its `watch` subscription in `close()`.

- **SES-2 (MUST)**: The key is `orvano.session.<projectId>@<host>[:<port>]<basePath>`: the lowercased host, the port only when it isn't the scheme's default, and the endpoint's base path without its trailing slash (for example `orvano.session.my-app@api.example.com:8443/orvano`). It never contains userinfo (CLN-1 rejects it). Two clients for different projects, servers, or base paths never share a key.
- **SES-3 (MUST)**: Default stores:
  - `orvano` on the web: `BrowserSessionStore` (`window.localStorage` through package:web, behind `if (dart.library.js_interop)`). The README states plainly that injected scripts (XSS) can read it, and that Safari may clear script written storage after about a week without a visit.
  - `orvano` on the Dart VM: `MemorySessionStore` (a pure Dart CLI or server process never writes a token to disk unasked).
  - `orvano_flutter` on iOS, Android, macOS, Windows, and Linux: `SecureSessionStore` (SES-4). On the web: `BrowserSessionStore` (flutter_secure_storage's web mode is experimental and is not used).
- **SES-4 (MUST)**: `SecureSessionStore` wraps flutter_secure_storage 11 in Orvano's own namespace, so it never reads, lists, or wipes the app's own secrets: on Android `AndroidOptions(storageNamespace: 'orvano', resetOnError: true)` (full namespace isolation; `resetOnError` is already the default and is set explicitly), so a value whose Keystore key is gone (after a backup restore) is wiped and reads as signed out instead of failing forever; on iOS `IOSOptions(accountName: 'orvano', accessibility: KeychainAccessibility.first_unlock_this_device)` and on macOS the same in `MacOsOptions` (keeping its defaults `usesDataProtectionKeychain: true` and `synchronizable: false`), so the item is readable by background work after the first unlock and never synced or moved to another device. On Windows and Linux, where the plugin has no namespace option, `keys()` filters `readAll()` by the `orvano.session.` prefix. Android uses the plugin's default cipher storage (RSA OAEP plus AES GCM with the key in the Android Keystore). These option names and defaults were confirmed in the 11.2.0 API docs on 2026-10-15 (the older `sharedPreferencesName` is gone in favor of `storageNamespace`), and 11.2.0 requires Dart 3.8 and Flutter 3.19, inside the floor. The nightly tests (DQA-4) prove `resetOnError` wipes only the Orvano namespace. The README lists each platform's setup: the macOS keychain entitlement (an unsigned build fails with error 34018 and shows as memory only), `libsecret` plus a running keyring service on Linux, and excluding the plugin's data from Android Auto Backup.
- **SES-5 (MUST)**: If the store fails (any error on read, write, or delete, such as Linux with no keyring service or a keychain still locked after a reboot), the client keeps the session in memory, logs a WARNING (DSC-3), and emits an `AuthState` whose `persistence` is `memoryOnly` and whose `storeFailure` says why. Sign in keeps working. The fallback isn't permanent: the client tries the store again on each `AppPhase.resumed` and before each write. When it works, a session signed in during the fallback is written (the newer session wins), or, if the client is signed out, a stored session is restored; then `persistence` goes back to `persistent`. The SDK never falls back to a plain file.
- **SES-6 (MUST)**: A stored value that reads fine but can't be parsed, or has an unknown `v`, is deleted and treated as signed out. This is not a store failure. (An Android value that can't be decrypted is handled by `resetOnError` in SES-4 and also reads as signed out.)
- **SES-7 (MUST)**: First run cleanup in `orvano_flutter` on native platforms, once per process (concurrent `open` calls share one run): if the `orvano.installed` marker is missing from `shared_preferences` (which an uninstall erases), delete every key with the prefix `orvano.session.` from the secure store, then write the marker, before restoring a session. A reinstalled app starts signed out even though the iOS keychain kept the old items. If reading the marker, deleting, or writing the marker fails, the marker is not written, a WARNING is logged, and the cleanup runs again on the next launch. Not run on the web.
- **SES-8 (MUST)**: `BrowserSessionStore.watch` reports changes from other tabs (the `storage` event), and the client updates its auth state when another tab signs in or out. Refresh races between tabs belong to feature 14.
- **SES-9 (MUST)**: Store writes for one key are serialized inside the client (a write never interleaves with a delete), and the last call wins. `close()` waits for the pending ones.

### Platforms

- **PLT-1 (MUST)**: `orvano` supports all six platforms: Android, iOS, macOS, Windows, Linux, and web, and is wasm ready. `lib/` never imports `dart:io`, `dart:html`, `dart:js`, `dart:js_util`, `dart:mirrors`, or `dart:ui`. Platform specific code sits behind conditional imports (`if (dart.library.js_interop)` for the web, `if (dart.library.io)` for native), and web code uses package:web and `dart:js_interop` only. `tool/check_imports.dart` enforces this.
- **PLT-2 (MUST)**: `orvano_flutter` supports the same six platforms. It sets no OS floor above Flutter's own minimums for its floor version and its plugins' minimums (flutter_secure_storage needs Android API 23, below Flutter's 24). _Verify at scaffold_ that flutter_secure_storage and shared_preferences are wasm ready on the web and support Swift Package Manager on iOS and macOS, since pana may count them when it scores `orvano_flutter`.
- **PLT-3 (MUST)**: `orvano_server` declares `platforms: { linux:, macos:, windows: }` in its pubspec, may use `dart:io`, never depends on Flutter, never uses `dart:mirrors`, and works under `dart compile exe` and in Orvano's Dart functions runtime (feature 37). A server key never belongs in a browser or phone app: the `dart:io` import already stops web builds from compiling, and `OrvanoServer` throws `UnsupportedError` on Android and iOS. The package doesn't claim those platforms (pana still awards full platform points).
- **PLT-4 (MUST)**: Web code works under both dart2js and dart2wasm, and CI tests both (DQA-2).

### pub.dev and documentation

- **PUB-1 (MUST)**: Every package scores full pub points. `tool/check_pana.dart` runs pana (`--json`; pana also has `--exit-code-threshold`, but that judges the total, not one category) on the latest stable SDK only (pana 0.23.19 needs Dart 3.11) on a copy of each package (pana modifies what it scores; the copy drops `resolution: workspace`) and fails if any category loses points, except "up to date dependencies", which can drop with no code change when a dependency ships a new major. A weekly scheduled job checks all categories and opens an issue when that one drops. Per PR, the copy of `orvano_flutter` points at the local `orvano` with a path override that exists only in the copy; at release, `orvano` is published first and `orvano_flutter` must then score in full before it is published (feature 52).
- **PUB-2 (MUST)**: Every public member has a doc comment (`public_member_api_docs`), every library has a library doc comment, and `[references]` resolve (`comment_references`). Each package has a README (what it is, install, a quick start, platform setup for `orvano_flutter`, the security notes from SES-3, SES-4, and NET-7, the retry worst case from NET-3, a recipe for testing app code with the real client plus `MockClient` and `MemorySessionStore`, and links to the docs site), a CHANGELOG entry for every release, and an `example/` that analyzes clean: pure Dart for `orvano` and `orvano_server`, a small Flutter app for `orvano_flutter`.
- **PUB-3 (MUST)**: Each pubspec sets `name`, `description` (60 to 180 characters), `version`, `repository`, `issue_tracker`, `documentation` (the docs site, feature 32, once it exists), and `topics` (at most five, for example `backend`, `database`, `auth`, `realtime`, `baas`). `LICENSE` and `NOTICE` sit in the package root (spec 0009 LIC-6), and pana must detect `LICENSE` as Apache 2.0.
- **PUB-4 (MUST)**: Public API changes are tracked from the first commit: before beta, every public API change gets a CHANGELOG line (breaking changes are still allowed). From the first beta, CI runs `dart_apitool diff` against the last release and fails on a breaking change without a major version bump (feature 52 sets the baseline). That includes the interface and sealed family changes in DRT-7.

### Testing

- **DQA-1 (MUST)**: Unit tests use package:test (`orvano`, `orvano_server`) and flutter_test (`orvano_flutter`). Test doubles are hand written: package:http's `MockClient` for plain responses (it ignores the abort trigger by design), a small `FakeAbortableClient` in the test helpers that honors the abort trigger and can hold a response open (for the timeout, cancel, and close tests), `MemorySessionStore` as a real in memory store, and small fakes for flutter_secure_storage, shared_preferences, and `AppLifecycleSource`. Time is controlled with `fake_async` and jitter with a seeded `Random` (STR-5). No mockito, mocktail, or build_runner. No real network outside the conformance suite, and no real delays.
- **DQA-2 (MUST)**: Per PR: `orvano` tests on the VM and in Chrome under dart2js and dart2wasm; `orvano_flutter` tests with `flutter test` plus `flutter test --platform chrome`, with and without `--wasm` (_verify at scaffold_ the flag); `orvano_server` tests on the VM on Linux; each against the floor and the latest stable, plus the downgrade job (DRT-3).
- **DQA-3 (MUST)**: Feature 10's shared conformance suite is implemented in Dart under `test/conformance/`, tagged `conformance`, and runs per PR for `orvano` (VM) and `orvano_server` against a real Orvano server container: the image the core monorepo's CI publishes, `ghcr.io/orvanohq/orvano` (spec 0007 IMG-3), pinned to the server version the SDK targets. Until the first image exists, the job is skipped with a visible notice in the CI summary. Locally the container runs under Docker or Podman (spec 0002 TST-5a, spec 0007 DEV-1); CI uses whatever its runners provide (feature 7).
- **DQA-4 (MUST)**: Nightly: `integration_test` from `packages/orvano_flutter/example` against a real server on an Android emulator, an iOS simulator, macOS, Windows, Linux (with a keyring service running), and Chrome (a wasm build). Since `integration_test` can't relaunch the app, a restart is simulated: sign in, `close()` the client, and open a new one over the same real platform storage, which must come back signed in. The first run cleanup is tested by clearing `shared_preferences` through the plugin and resetting the once per process guard (a `@visibleForTesting` hook) before opening again, which must come back signed out. A Linux job with no keyring service proves the memory fallback. The server SDK runs its tests nightly on macOS and Windows too, plus a smoke test of `dart compile exe example/main.dart` against the server. The latest Flutter beta also runs nightly, for information only. A nightly failure opens an issue (feature 7 wires it).
- **DQA-5 (MUST)**: No coverage number. Instead, each kind of code has required tests, and review checks them:
  - Each hand written service method: success, and each `OrvanoException` subtype it can produce.
  - Retry: which methods and statuses retry, TLS failures not retried, `Retry-After` in both forms and above the cap, no retry after a cancel or during `close()`, fresh requests per attempt.
  - Auth hook: the header on every attempt, one refresh and one retry on a session expired 401, sign out after a second 401.
  - Timeout, cancel, and close: a timeout gives `OrvanoTimeoutException`; a cancel before sending, a cancel mid flight, and `close()` mid flight give `OrvanoCancelledException`, also with a client that ignores aborting.
  - Each `SessionStore`: read, write, and delete round trip; an unparseable value (SES-6); a failing store and its recovery (SES-5); the first run cleanup and its failure path (SES-7); cross tab changes (SES-8, in Chrome).
  - Auth state: a late listener gets the current state first; no event on a token refresh; streams close on `close()`.
  - Models and codecs: unknown fields ignored, a missing or mistyped field gives `OrvanoProtocolException` with its path, an unknown enum gives `unknown`, whole numbers read the same on the VM and the web, a codec round trips.
- **DQA-6 (SHOULD)**: Test names describe behavior (`'retries a GET once after a 503'`), with one `group` per class under test.

### Security and logging

- **DSC-1 (MUST)**: `orvano` and `orvano_flutter` never accept an API key (there is no such parameter), and the generator puts no `server` only operation into them (the `x-orvano-audience` tag, spec 0014 XTN-1, GNR-4).
- **DSC-2 (MUST)**: Tokens and keys never appear in logs, exception messages, or `toString()` output. `OrvanoServer`, the session, and the request options redact them.
- **DSC-3 (MUST)**: Logging uses package:logging under `Logger('orvano')` and its children (`orvano.http`, `orvano.auth`, `orvano.session`). With no handler attached, nothing is printed. Request lines go at `FINE` with method, path (no query string, which may hold filter values), status, and duration, never headers or bodies. Store failures and failed first run cleanups log at `WARNING`. No `print` in `lib/` (`avoid_print`).
- **DSC-4 (MUST)**: No certificate pinning or custom trust in the SDK; apps that need it pass their own `http.Client` (NET-1).

### Canonical pattern

One flow end to end. Endpoint paths, header names, and error codes are placeholders until features 10, 14, and 16 decide them; the shape is the standard. Package APIs marked _verify at scaffold_ above may need small adjustments.

The pure Dart package's pubspec (version floors are examples; the scaffold sets each to the lowest version that works, DRT-3):

```yaml
name: orvano
description: Dart client for Orvano, the self hostable backend. Auth, databases, storage, and realtime for Dart and Flutter apps.
version: 0.1.0
repository: https://github.com/orvanohq/sdk-flutter/tree/main/packages/orvano
issue_tracker: https://github.com/orvanohq/sdk-flutter/issues
topics: [backend, database, auth, realtime, baas]
resolution: workspace

environment:
  sdk: ^3.10.0

dependencies:
  clock: ^1.1.0
  collection: ^1.18.0
  http: ^1.5.0
  logging: ^1.2.0
  meta: ^1.15.0
  web: ^1.1.0

dev_dependencies:
  fake_async: ^1.3.0
  lints: ^6.0.0
  test: ^1.25.0
```

The public library exports with `show` lists (DRT-8):

```dart
/// Dart client for Orvano.
library;

export 'src/core/cancel_token.dart' show OrvanoCancelReason, OrvanoCancelToken;
export 'src/core/codec.dart' show OrvanoCodec;
export 'src/core/exceptions.dart'
    show
        OrvanoApiException,
        OrvanoCancelledException,
        OrvanoException,
        OrvanoNetworkException,
        OrvanoProtocolException,
        OrvanoRateLimitedException,
        OrvanoTimeoutException;
export 'src/core/options.dart'
    show OrvanoClientOptions, OrvanoRequestOptions, RetryOptions;
export 'src/generated/error_codes.dart' show OrvanoErrorCodes;
export 'src/orvano.dart' show AppLifecycleSource, AppPhase, Orvano;
export 'src/services/auth.dart'
    show AuthSignedIn, AuthSignedOut, AuthState, SessionPersistence;
export 'src/services/databases.dart' show OrvanoRow, ResultPage, TableHandle;
export 'src/session/session_store.dart'
    show MemorySessionStore, SessionStore, SessionStoreFailure;
```

The exception tree (EXC-1). Class modifiers only restrict code outside this library, so the subtypes can extend each other here:

```dart
/// Every runtime failure the Orvano SDK reports.
sealed class OrvanoException implements Exception {
  const OrvanoException(this.message);

  /// A short description, safe to log.
  final String message;
}

/// The server answered with an error.
final class OrvanoApiException extends OrvanoException {
  const OrvanoApiException({
    required this.statusCode,
    required this.code,
    required this.title,
    required this.rawBody,
    this.detail,
    this.traceId,
  }) : super(title);

  final int statusCode;
  final String code;
  final String title;
  final String? detail;
  final String? traceId;
  final String rawBody;

  @override
  String toString() =>
      'OrvanoApiException($statusCode $code, traceId: $traceId): $title';
}

/// The server answered 429 and asked the caller to slow down.
final class OrvanoRateLimitedException extends OrvanoApiException {
  const OrvanoRateLimitedException({
    required super.statusCode,
    required super.code,
    required super.title,
    required super.rawBody,
    super.detail,
    super.traceId,
    this.retryAfter,
  });

  final Duration? retryAfter;
}

// OrvanoNetworkException, OrvanoTimeoutException, OrvanoCancelledException,
// and OrvanoProtocolException follow the same shape.
```

The cancel token (NET-5). The transport registers one callback per attempt and removes it afterwards:

```dart
/// Cancels one or more Orvano calls.
final class OrvanoCancelToken {
  final Completer<void> _cancelled = Completer<void>();
  final Set<void Function()> _callbacks = {};
  Object? _reason;

  /// Whether [cancel] has been called.
  bool get isCancelled => _cancelled.isCompleted;

  /// The reason given to [cancel], if any.
  Object? get reason => _reason;

  /// Completes when [cancel] is called.
  Future<void> get whenCancelled => _cancelled.future;

  /// Cancels every call using this token. Later calls do nothing.
  void cancel([Object? reason]) {
    if (_cancelled.isCompleted) return;
    _reason = reason;
    _cancelled.complete();
    for (final callback in [..._callbacks]) {
      callback();
    }
    _callbacks.clear();
  }

  /// Runs [onCancel] when this token is cancelled. Call the returned function
  /// to stop listening.
  @internal
  void Function() register(void Function() onCancel) {
    _callbacks.add(onCancel);
    return () => _callbacks.remove(onCancel);
  }
}
```

One attempt inside the transport, abridged (NET-2, NET-4, NET-5, NET-8, NET-10, EXC-2). The retry loop (NET-3) calls it once per attempt and decides whether `_AttemptTimedOut` or a network error is worth another try. `_inFlight` lets `close()` abort every attempt (CLN-5):

```dart
Future<http.Response> _attempt(
  _RequestSpec spec, {
  required Duration timeout,
  required OrvanoCancelToken? cancelToken,
}) async {
  if (cancelToken case OrvanoCancelToken(isCancelled: true, :final reason)) {
    throw OrvanoCancelledException(reason);
  }

  final abort = Completer<void>();
  void fire() {
    if (!abort.isCompleted) abort.complete();
  }

  final timer = Timer(timeout, fire);
  final unregister = cancelToken?.register(fire);
  _inFlight.add(fire);

  final request = http.AbortableRequest(
    spec.method,
    _endpoint.resolve(spec.relativePath),
    abortTrigger: abort.future,
  )
    ..followRedirects = false // NET-10: refused on every platform
    ..headers.addAll(_defaultHeaders)
    ..headers.addAll(await _auth.headers())
    ..headers.addAll(spec.headers);
  final body = spec.body;
  if (body != null) request.bodyBytes = body;

  try {
    // Race against the trigger, so a client that ignores aborting still
    // releases the caller on time. Future.any ignores the loser's later error.
    return await Future.any([
      _client.send(request).then(http.Response.fromStream),
      abort.future.then<http.Response>((_) => throw const _Aborted()),
    ]);
  } on OrvanoException {
    rethrow;
  } on Object catch (error, stackTrace) {
    // RequestAbortedException extends ClientException, so check it first.
    if (error is _Aborted || error is http.RequestAbortedException) {
      if (cancelToken case OrvanoCancelToken(isCancelled: true, :final reason)) {
        throw OrvanoCancelledException(reason);
      }
      if (_closed) {
        throw const OrvanoCancelledException(OrvanoCancelReason.clientClosed);
      }
      throw _AttemptTimedOut(timeout);
    }
    Error.throwWithStackTrace(OrvanoNetworkException(error), stackTrace);
  } finally {
    timer.cancel();
    unregister?.call();
    _inFlight.remove(fire);
  }
}
```

Reading a model safely (MDL-2): one field at a time, reporting the path and the expected type, never the value:

```dart
factory Todo.fromJson(Map<String, Object?> json, [String path = r'$']) {
  final id = switch (json['id']) {
    final String value => value,
    final other => throw OrvanoProtocolException.field('$path.id', 'String', other),
  };
  final title = switch (json['title']) {
    final String value => value,
    final other => throw OrvanoProtocolException.field('$path.title', 'String', other),
  };
  final note = switch (json['note']) {
    null => null,
    final String value => value,
    final other => throw OrvanoProtocolException.field('$path.note', 'String?', other),
  };
  final priority = switch (json['priority']) {
    final num value when value == value.truncate() => value.toInt(),
    final other => throw OrvanoProtocolException.field('$path.priority', 'int', other),
  };
  return Todo(id: id, title: title, note: note, priority: priority);
}

static final OrvanoCodec<Todo> codec =
    OrvanoCodec.of(fromJson: Todo.fromJson, toJson: (todo) => todo.toJson());
```

(`OrvanoProtocolException.field` records only the runtime type of `other`, never its value.)

A Flutter app opens the client, reads typed rows, cancels when the screen closes, and handles every error (CLN-1, MDL-5, NET-5, EXC-1):

```dart
Future<void> main() async {
  final orvano = await OrvanoFlutter.open(
    endpoint: Uri.parse('https://orvano.example.com'),
    projectId: 'my-app',
  );
  runApp(TodoApp(orvano: orvano));
}

class _TodoListState extends State<TodoList> {
  final _cancel = OrvanoCancelToken();
  late final TableHandle<Todo> _todos;
  List<Todo> _items = const [];

  @override
  void initState() {
    super.initState();
    _todos = widget.orvano.databases.table('todos').withCodec(Todo.codec);
    unawaited(_load()); // errors are handled inside _load
  }

  Future<void> _load() async {
    try {
      final page = await _todos.list(
        limit: 50,
        options: OrvanoRequestOptions(cancelToken: _cancel),
      );
      if (!mounted) return;
      setState(() => _items = page.items);
    } on OrvanoException catch (e) {
      if (!mounted) return;
      final message = switch (e) {
        OrvanoApiException(code: OrvanoErrorCodes.notFound) => 'This table is gone.',
        OrvanoApiException(:final title) => title,
        OrvanoNetworkException() => 'You seem to be offline.',
        OrvanoTimeoutException() => 'The server took too long.',
        OrvanoCancelledException() => null,
        OrvanoProtocolException() => 'Please update the app.',
      };
      if (message != null) _showError(message);
    }
  }

  @override
  void dispose() {
    _cancel.cancel('screen closed');
    super.dispose();
  }
}
```

Auth state drives the first screen (STR-2, CLN-7):

```dart
StreamBuilder<AuthState>(
  stream: orvano.auth.stateChanges,
  initialData: orvano.auth.currentState,
  builder: (context, snapshot) => switch (snapshot.requireData) {
    AuthSignedIn() => const HomeScreen(),
    AuthSignedOut() => const SignInScreen(),
  },
)
```

A server process (CLN-1, PLT-3). Missing configuration fails loudly instead of defaulting to empty strings:

```dart
String requireEnv(String name) =>
    Platform.environment[name] ?? (throw StateError('$name is not set'));

Future<void> main() async {
  final orvano = OrvanoServer(
    endpoint: Uri.parse(requireEnv('ORVANO_ENDPOINT')),
    projectId: requireEnv('ORVANO_PROJECT_ID'),
    apiKey: requireEnv('ORVANO_API_KEY'),
  );
  try {
    final users = await orvano.users.list(limit: 100);
    // ...
  } finally {
    await orvano.close();
  }
}
```

A unit test with a hand written double and fake time (DQA-1, NET-3):

```dart
test('retries a GET once after a 503, then returns the page', () {
  fakeAsync((async) {
    var calls = 0;
    final client = MockClient((request) async {
      calls++;
      return calls == 1
          ? Response('', 503)
          : Response(jsonEncode(onePageOfRows), 200);
    });

    ResultPage<OrvanoRow>? page;
    // Not awaited: fakeAsync drives it with elapse below.
    unawaited(
      Orvano.open(
        endpoint: Uri.parse('https://orvano.test'),
        projectId: 'p1',
        httpClient: client,
        sessionStore: MemorySessionStore(),
      )
          .then((orvano) => orvano.databases.table('todos').list())
          .then((result) => page = result),
    );

    async.elapse(const Duration(seconds: 5));

    expect(calls, 2);
    expect(page?.items, hasLength(1));
  });
});
```

### Replaces

These common patterns are wrong in the Orvano Dart SDKs, even when a tutorial or an agent skill suggests them:

- dio, chopper, retrofit, or calling `HttpClient` from `dart:io` directly; `RetryClient`; `Future.timeout` around a request; `response.body` for JSON
- A global `Orvano.instance`, `initialize()` singletons, get_it, or any service locator inside the SDK
- rxdart, `BehaviorSubject`, or a dependency on Provider, Riverpod, Bloc, GetX, or signals
- json_serializable, freezed, built_value, or build_runner in an SDK package; `Map<String, dynamic>` and `dynamic` access; unchecked `as` casts on JSON; `double` or `int` patterns on JSON numbers
- Public types named `Row`, `Page`, or anything else that clashes with Flutter's widgets library
- `compute()` or `Isolate.run` inside the SDK
- Throwing raw `ClientException`, `SocketException`, `FormatException`, strings, or `Exception('...')`
- Storing tokens in `shared_preferences`, Hive, a plain file, flutter_secure_storage's web mode, or flutter_secure_storage's default namespace
- `dart:html`, `dart:js`, `dart:js_util`, or `dart:io` in `orvano`'s `lib/`
- mockito, mocktail, real network calls in unit tests, `sleep` or real delays in tests
- `print`, logging headers, bodies, or query strings, `DateTime.now()` in `lib/`
- Native plugin code in an Orvano package; melos; `part` files; exporting `src/` without `show`

### Enforcement

Feature 7 installs and wires these; this table says what each rule is checked by.

| Mechanism                                                                                          | Rules                                                                                                                                                                                                                               | Where it lives                                                          |
| -------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| Analyzer with strict modes and the DRT-4 lint list, `--fatal-infos`                                | DRT-4, DRT-6 (`dynamic`, casts), DRT-8, STR-1, STR-4, PUB-2, DSC-3 (`avoid_print`)                                                                                                                                                  | Shared `analysis_options.yaml` plus the `example/` and `tool/` ones, CI |
| `dart format --set-exit-if-changed`                                                                | DRT-5                                                                                                                                                                                                                               | CI                                                                      |
| `tool/check_imports.dart`                                                                          | PLT-1, PLT-3 (`dart:mirrors`, Flutter imports), STR-5 (`DateTime.now` in `lib/`), STR-6 (`compute`, `Isolate.run`), DRT-8 (`part`), NET-10 (`.body` on a response in the transport)                                                 | Each repo, CI                                                           |
| `tool/check_deps.dart` (pubspecs against the DRT-2 list, no `dependency_overrides`, PKG-4's range) | DRT-2, DRT-3, PKG-4, PKG-5 (no plugin folders), Replaces list (banned packages)                                                                                                                                                     | Each repo, CI                                                           |
| `tool/check_version.dart`                                                                          | NET-6 (version constant matches pubspec)                                                                                                                                                                                            | Each repo, CI                                                           |
| Downgrade job, latest versions job, floor and latest SDK matrix                                    | DRT-1, DRT-3, DQA-2                                                                                                                                                                                                                 | CI                                                                      |
| `tool/check_pana.dart` per PR, plus the weekly full check                                          | PUB-1, PUB-3, PLT-1 (wasm ready), PLT-2, PLT-3                                                                                                                                                                                      | CI                                                                      |
| Unit tests (VM, Chrome js and wasm, flutter test)                                                  | DQA-1, DQA-2, DQA-5, EXC-2, and the behavior rules NET, EXC, MDL, SES, STR                                                                                                                                                          | CI per PR                                                               |
| Conformance suite against a real server                                                            | DQA-3, PKG-3 (both repos behave alike)                                                                                                                                                                                              | CI per PR                                                               |
| Nightly integration tests on six platforms, server OS matrix, compile smoke test                   | DQA-4, SES-4, SES-5, SES-7, PLT-3                                                                                                                                                                                                   | CI nightly                                                              |
| `dart_apitool diff` (from the first beta)                                                          | PUB-4, DRT-7                                                                                                                                                                                                                        | CI                                                                      |
| Review (`/check review`, people)                                                                   | Everything else: class modifiers (DRT-7), `!` comments (DRT-6), public API shape (CLN), EXC-2 mapping beyond tests, retry and timeout behavior beyond tests, redaction (DSC-2, EXC-5), README content (PUB-2), test quality (DQA-6) | PR template checklist from feature 7                                    |

### Rollout

New code immediately. Neither Dart SDK repo exists yet, so every rule holds from the first commit, and the analyzer, format, `tool/` checks, pana, and the downgrade job are on before the first service lands. The conformance job is wired from the start and runs once the first server image exists (DQA-3).

### Exceptions

- Tests may use `!`, `DateTime.now()` when the test isn't about time, and `dynamic` in test helpers that build raw JSON.
- `tool/` scripts may use `dart:io` and `print` (their own analysis options, DRT-4).
- `example/` code may use `print` and reads secrets from the environment (its own analysis options, DRT-4).
- `orvano_flutter` exports all of `orvano` without a `show` list (DRT-8, PKG-4).
- Generated code may break naming lints where the contract's names require it (`// ignore_for_file:` naming rules only, at the top of the file); every other rule still applies.
- Anything else: a `// Deviation <RULE-ID>: <reason>` comment and a PR mention (DRT-0).

## Consequences

**Positive**:

- One runtime template behind both repos, plus the conformance suite, means the Flutter, pure Dart, and server SDKs fail, retry, time out, and cancel the same way, and match the .NET SDK.
- Apps can `switch` exhaustively over every SDK error and never meet a platform specific exception type.
- The pure Dart `orvano` package runs in Dart CLIs, Jaspr, and tests without Flutter.
- Small, mostly Dart team dependencies (http, meta, clock, logging, collection, web) mean apps rarely hit version conflicts because of Orvano.
- A namespaced keychain store with first run cleanup, corrupt value handling, and a visible, self healing memory fallback covers the failure cases that usually surface as "users randomly signed out" bug reports, without touching the app's own secrets.
- Cancels and timeouts release the caller on time even with an HTTP client that can't abort.
- Full pub points, honest platform claims, and documented members make the packages look as trustworthy as they are.

**Negative / tradeoffs**:

- Two packages in the Flutter repo mean two pub.dev pages, versions kept in step, and a layer that passes `orvano` through to maintain. The repo's CI needs a Flutter SDK even for the pure Dart package.
- Separate copies of the runtime mean `orvano` and `orvano_server` each have their own `OrvanoException`, `OrvanoCancelToken`, and options types. An app that uses both (a full stack Dart app) must import one with a prefix and catch each type separately.
- Retry, timeouts, cancel, the auth hook, the state stream helper, model equality, and `copyWith` are Orvano code (in feature 10's templates) instead of a library's, and need their own tests.
- Plain generated models mean feature 10's generator must emit `==`, `hashCode`, `copyWith`, and field by field parsing itself; `copyWith` can't clear a nullable field.
- The Dart 3.10 floor keeps primary constructors and later language features out until about November 2027.
- Browser storage on the web can be read by injected scripts; the docs say so, but a web app with an XSS hole leaks its session.
- No isolate decoding means a very large page could drop frames on slow phones; STR-6 only revisits it after profiling.
- Sealed families and interfaces are frozen after beta: a new `AuthState` or a new `SessionStore` method means a major version.
- A client that ignores aborting can leave a connection running in the background after the caller has been released.
- Nightly device tests across six platforms need macOS runners, emulators, and a keyring service on Linux, which cost CI minutes and setup in feature 7.
- A few rules still carry _verify at scaffold_ notes (test flags, one lint's casing, plugin Swift Package Manager support, lowest versions); the first commit may have to adjust their exact form.
- The floor CI job resolves older releases of `test` and `shared_preferences`, because their newest releases already need Dart 3.11, so the floor and latest jobs don't run identical test tooling.

**Neutral**:

- Contributors used to dio, freezed, Riverpod inside packages, or mockito need to adjust; the Replaces list names the swaps.
- Some installed skills partly conflict with this spec (see `## Decision`); agents apply DRT-0.
- The client entry point became `await Orvano.open(...)` rather than a plain constructor so the stored session is read before the first frame; it is still an explicit instance with no global.
- `OrvanoRow` and `ResultPage` are longer than `Row` and `Page`, to avoid clashing with Flutter.

## Follow-up

- [x] Confirmed on 2026-10-15 (no SDK repo needed): package:http's `AbortableRequest`, `AbortableStreamedRequest`, `abortTrigger`, and `RequestAbortedException` (NET-2), and that `MockClient` ignores aborting (DQA-1); the browser client honors `followRedirects` (NET-10); flutter_secure_storage 11.2's `storageNamespace`, `resetOnError` (default true), `accountName`, `accessibility`, `first_unlock_this_device`, `readAll`, and its Dart 3.8 and Flutter 3.19 floor (SES-4); pana's `--json` and `--exit-code-threshold` flags and its Dart 3.11 need (PUB-1); 20 of the 21 DRT-4 lint names; newest `test` and `shared_preferences` need more than the floor (DRT-3).
- [ ] First commit of each Dart SDK repo, still verify: the casing of `no_runtimeType_toString` (DRT-4); flutter_secure_storage and shared_preferences Swift Package Manager support (sources conflict) and wasm readiness (PLT-2); the shape of pana's JSON report for `tool/check_pana.dart` and whether the total maximum is 150 or 160 (PUB-1); the `dart test` syntax for running Chrome under both dart2js and dart2wasm, and whether `flutter test --platform chrome --wasm` exists (DQA-2; if not, test the wasm build in the nightly only and note it here); the lowest working versions of `meta`, `web`, `clock`, `logging`, `collection`, `fake_async`, `lints`, `test`, and `shared_preferences` that resolve on the floor (DRT-3). Record any changed form in this spec.
- [x] Feature 6: name the Flutter SDK repo and the Dart server SDK repo in the `orvanohq` org; publish the server container image the conformance suite pulls (DQA-3). Closed by spec 0007: repos `sdk-flutter` and `sdk-dart` (REP-3), image `ghcr.io/orvanohq/orvano` (IMG-3).
- [x] Serve the CORS headers below. Closed by spec 0007 COR-1 to COR-5: the server allows Orvano's request headers and exposes `Retry-After`, the trace header, and Orvano's response headers from one `OrvanoHeaders` list that feature 10 fills.
- [ ] Feature 7 (`/audit`): lift these rules into each Dart SDK repo's `AGENTS.md`; record the 10 installed skills under `## Agent skills` with the DRT-0 overrides; list the official Dart and Flutter MCP server (`dart mcp-server`, Dart 3.9 or later) as the MCP server to connect in those repos. Write the shared `analysis_options.yaml` and the `tool/` checks, and wire the CI jobs in the Enforcement table, including macOS runners, a Linux keyring service for DQA-4, the weekly pana job, and nightly issue filing.
- [x] Feature 10: one template set for the Dart runtime emitted into both repos (PKG-3), with the auth hook (NET-8) and the streamed primitive (NET-9); Dart output that meets MDL-1 to MDL-4 (plain classes, field by field parsing, enums with `unknown`, UTC timestamps, `num` reads); separate response and input models (MDL-3); a 64 bit integer wire format that is safe on the web (MDL-4); how partial updates mark a field as unset; client only and server only endpoint tags (DSC-1); the SDK identification header name (NET-6); the reserved `unknown` error code (NET-10) and the session expired code (NET-8); error body fields matching EXC-1; contract descriptions good enough to become dartdoc (PUB-2); the conformance suite runnable from `dart test` (DQA-3); the CORS contract: Orvano's request headers in `Access-Control-Allow-Headers`, and `Retry-After` plus the trace header in `Access-Control-Expose-Headers`, so web clients can honor retries and read trace IDs. Settled in spec 0014 (2026-10-25): GNR-5, JSW-5, JSW-9, JSW-10, XTN-1, HDR-1 to HDR-3, PRB-1, PRB-8, CNF, and the CORS lists in HDR-2.
- [ ] Feature 14: the session's JSON shape and its `v` schema version (SES-1); every `AuthState` subtype and payload before 1.0 (STR-2, DRT-7); how a refresh works for NET-8 and on `AppPhase.resumed` (CLN-6); refresh races across tabs (SES-8) and between a background isolate and the app (STR-6).
- [ ] Feature 52: reserve `orvano`, `orvano_flutter`, and `orvano_server` on pub.dev early (names are first come, first served); set up a verified publisher; release the two Flutter repo packages in step, `orvano` first (PKG-4, PUB-1); set the `dart_apitool` baseline from the first beta (PUB-4).
- [ ] Feature 56: generated row classes expose a static `codec` (MDL-5); decide where system fields sit.
- [x] Feature 57: the LICENSE file each package needs for pub points (PUB-3). (Spec 0009 LIC-6.)
- [ ] Feature 5: confirm or tighten SES-3 (browser storage on the web) and NET-7 (the insecure endpoint opt in).
- [ ] Each November: apply the DRT-1 floor rule (November 2026 keeps 3.10).

## Rationale

Reasoning, options, research, and sources: see [rationale.md](rationale.md).
