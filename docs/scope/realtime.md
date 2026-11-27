# Scope: Realtime

Live events, presence and broadcast over one WebSocket per client. Part of the [Orvano scope](index.md).

## M4 (Phase 2: Core services)

### 49. Realtime connection & events · needs a decision · Beta
One socket, a small JSON protocol, and row, file and account events checked against the same rules as the REST API. A gap here leaks data, so it gets the stricter tier.
**Done when:** a client opens one WebSocket at `/v1/realtime` and subscribes to `db.{table}`, `db.{table}.{rowId}`, `storage.{bucket}` and `account`; table subscriptions accept equality filters joined with AND; events come from the outbox; each event is checked per subscriber with the permission engine before delivery.
- [ ] Design it (spec): `/architect realtime connection & events`

### 50. Presence & broadcast · needs a decision
Who is in a room right now, and short lived messages to everyone in it.
**Done when:** clients join and leave `presence.{room}` with a small state object that is cleaned up on disconnect; `broadcast.{room}` delivers messages that are never stored and are size and rate limited; room rules are set per room prefix and default to signed in users.
- [ ] Design it (spec): `/architect presence & broadcast`

### 51. Resume after reconnect · needs a decision
A client that drops and comes back does not miss events.
**Done when:** a client that reconnects with its last event id receives the events it missed from a short buffer (about 2 minutes); a longer gap is reported so the client can fetch again.
- [ ] Design it (spec): `/architect resume after reconnect`

### 52. Realtime in the SDKs · needs a decision
The hand written socket clients for Dart and TypeScript.
**Done when:** Dart exposes channels as a `Stream` and TypeScript as a callback or an async iterator; both reconnect and resume on their own; the docs show a `StreamBuilder` example and a React hook.
- [ ] Design it (spec): `/architect realtime in the sdks`

### 53. Realtime console · needs a decision
A window into what is flowing right now.
**Done when:** the console shows a live event inspector and the active connection count.
- [ ] Design it (spec): `/architect realtime console`

## Deferred
- **Offline sync**: local changes that merge when back online
- **Full filter syntax**: more than equality filters on subscriptions
- **Change streams to external sinks**: events pushed to outside systems
- **Function execution events**: live events for function runs
