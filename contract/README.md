# contract

The API contract, the one source of truth for the SDKs, the CLI, the MCP tools and the docs.

What lands here, starting with feature 9 (API contract):

- `openapi.json`: the OpenAPI 3.1 document generated from the `orvano` endpoints and committed.
- `events.yaml`: the event payloads modules publish.
- `errors.yaml`: the error codes and the shared error shape.

Nothing here is written by hand once feature 9 lands. The server generates it, and the contract tests check it.
