# Epic: Realtime

Live updates over one connection per client. See [index.md](index.md) for the full plan.

### 22. Realtime subscriptions · needs a decision
Subscribe from the client SDKs to database row changes, file events, and auth events, filtered by query and by permissions (a user only receives what they are allowed to read). Builds the internal event bus that webhooks, functions, and notifications reuse.
**Done when:** a row inserted from a .NET server appears live in a Next.js and a Flutter app, and a user without read access never receives it.
- [ ] Design it (spec): `/architect realtime subscriptions`

### 23. Broadcast & presence
Client to client messages on named channels (chat, cursors, game moves) and presence (who is online, typing, viewing), with expiry and permissions.
**Done when:** two clients exchange messages on a channel, presence shows who is online, and a closed app drops out of presence automatically.
- [ ] Build it: `/develop broadcast & presence`
