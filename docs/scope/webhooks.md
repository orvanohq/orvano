# Epic: Webhooks platform ★

Orvano's second differentiator: webhooks built like a product, not a checkbox. Appwrite sends basic event webhooks; Orvano adds signing, retries, replay, delivery logs, and inbound endpoints. See [index.md](index.md) for the full plan.

### 31. Outbound webhooks ★ · needs a decision
Subscribe endpoints to any platform event, with signed payloads, retries with backoff, a delivery log with request and response, one click replay, and secret rotation.
**Done when:** a failed delivery retries on schedule, you can inspect and replay any delivery from the console, and receivers verify the signature with a helper in every server SDK.
- [ ] Design it (spec): `/architect outbound webhooks`

### 32. Inbound webhooks ★
Give each project verified endpoints that accept webhooks from outside services (payments, Git hosts, form tools), check their signatures, log them, and hand them to a function or job.
**Done when:** an inbound webhook with a valid signature triggers a function, an invalid one is rejected and logged, and you can replay a stored inbound event.
- [ ] Build it: `/develop inbound webhooks`
