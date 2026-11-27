# Scope: Webhooks

Signed HTTPS callbacks for events, with retries, a delivery log and replay. Part of the [Orvano scope](index.md).

## M5 (Phase 2: Core services)

### 62. Webhook endpoints & signed delivery · needs a decision
Subscribe a URL to event patterns and receive signed deliveries.
**Done when:** an HTTPS URL can subscribe to event patterns (such as `db.posts.*`); each delivery is signed in the Standard Webhooks format and its id works as an idempotency key; endpoints, event selection and secret rotation are managed from the API and the console.
- [ ] Design it (spec): `/architect webhook endpoints & signed delivery`

### 63. Webhook retries, log & replay · needs a decision
What happens when the other side is down, and how you see and repair it.
**Done when:** failed deliveries retry with exponential backoff for up to 24 hours; after repeated failures the webhook turns itself off and the instance admin gets an email; the delivery log keeps request, response and latency for 7 days; replay and send test event work from the API and the console.
- [ ] Design it (spec): `/architect webhook retries, log & replay`

## Deferred
- **Filter expressions**: conditions on the event payload
- **Fan out to queues**: deliver to a queue instead of a URL
- **Longer log retention**: beyond 7 days
