# Scope: SDKs

The generator and the packages for TypeScript, Next.js, Dart, Flutter and C#. Each is a hand written core plus service classes generated from the contract. Service features in other files add their own SDK calls as they land. Part of the [Orvano scope](index.md).

## M2 (Phase 1: Foundation)

### 17. SDK generator · needs a decision
Orvano's own generator, so SDK code reads like hand written code and every surface stays in sync with the contract.
**Done when:** `tools/Orvano.SdkGen` reads the contract and the event catalog and writes service classes for TypeScript and Dart; method names, grouping and the client or server surface come from the `x-orvano-sdk` hints; CI regenerates and fails on drift.
- [ ] Design it (spec): `/architect sdk generator`

### 21. TypeScript SDK core · needs a decision
The `orvano` npm package: one package for browser, React Native, Node, Bun and Deno. The console is built on it.
**Done when:** the package signs in, keeps and refreshes a session, retries safely and calls generated services with nothing beyond `fetch` and `WebSocket`; `orvano/server` exposes the server key surface and throws in a browser; integration tests run against a real Orvano.
- [ ] Design it (spec): `/architect typescript sdk core`

### 22. Dart SDK core · needs a decision
The pure Dart `orvano` package, for Dart servers and as the base of the Flutter SDK.
**Done when:** the package works in client mode and server key mode, covers auth sessions and generated services, and passes integration tests against a real Orvano.
- [ ] Design it (spec): `/architect dart sdk core`

### 25. Next.js helpers · needs a decision
The `orvano/next` entry point for server rendering with cookie sessions.
**Done when:** `orvano/next` gives `createServerClient()` for Server Components, Server Actions and Route Handlers, cookie sessions, a middleware that refreshes tokens and `<OrvanoProvider>`; a sample app signs in and reads data on the server.
- [ ] Design it (spec): `/architect next.js helpers`

### 26. Flutter SDK · needs a decision
The `orvano_flutter` package, with the native pieces a mobile app needs.
**Done when:** the package wraps the Dart SDK with native Google and Apple sign in, secure token storage, deep link handling for magic links and `AuthStateBuilder`; a sample app signs in on iOS and Android.
- [ ] Design it (spec): `/architect flutter sdk`

## M6 (Phase 2: Core services)

### 69. C# SDK
`Orvano.Sdk` for server side C#, from the same generator and pattern as the other SDKs.
**Done when:** the package covers the server key surface for every v1 service; `orvano types --lang cs` writes typed models; integration tests pass.
- [ ] Build it: `/develop c# sdk`

## Deferred
- **Per SDK mirror repos**: read only copies, if SDK users ask for them
