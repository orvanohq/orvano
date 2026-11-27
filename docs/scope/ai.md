# Scope: AI layer

What makes Orvano agent first: the MCP server, agent tokens, the approval queue, the skills pack and the gate that proves an agent can build an app alone. Part of the [Orvano scope](index.md).

## M3 (Phase 1: Foundation)

### 38. Agent tokens · needs a decision · Beta
Scoped, expiring tokens for agents, so an agent never holds a root key.
**Done when:** an agent token (`ora_…`) is bound to one project, scoped, expires (8 hours by default), can be marked dry run only and records the person who issued it; it never bypasses permission rules without the `admin` scope; every action it takes is tagged with the agent's name in the audit log; it can be revoked.
- [ ] Design it (spec): `/architect agent tokens`

### 39. MCP server v0 · needs a decision
The first agent thread: an MCP server inside the API with the discover and database tools.
**Done when:** `/mcp` answers over stateless Streamable HTTP inside the API with agent token auth; the discover and database tools (`get_project_overview`, `describe_schema`, `plan_migration`, `apply_migration`, `query_rows`, `insert_rows`, `set_table_permissions`, `generate_types`) are generated from operations tagged `x-mcp`; every mutating tool supports dry run; destructive changes are refused with a hint until the approval queue exists; a coding agent can create a table and query it.
- [ ] Design it (spec): `/architect mcp server v0`

## M6 (Phase 2: Core services)

### 70. Skills pack · needs a decision
The 11 starter skills that teach an agent how to use Orvano correctly.
**Done when:** the public `skills` repo holds the 11 starter skills, each stating when to use it, the exact SDK calls, common mistakes and a finishing checklist; `orvano skills install|update` copies them into the right place for each agent; the MCP tool `get_skill` serves the same content.
- [ ] Design it (spec): `/architect skills pack`

### 72. Skills eval harness · needs a decision
Proof in CI that each skill still leads an agent to a working app.
**Done when:** CI runs a coding agent against each skill and checks that the resulting app builds; results are readable per skill; a failing skill blocks its release.
- [ ] Design it (spec): `/architect skills eval harness`

## M7 (Phase 2: Core services)

### 74. OAuth sign in for agents · needs a decision · Beta
Remote MCP clients sign in through standard OAuth instead of a pasted token.
**Done when:** a remote MCP client can register itself, send the person through an OAuth 2.1 sign in and consent screen, and receive an agent token bound to one project with an expiry; the person can see and revoke it in the console.
- [ ] Design it (spec): `/architect oauth sign in for agents`

### 75. Approval queue · needs a decision · Beta
Destructive changes from an agent wait for a person. This is the core of the safety model.
**Done when:** a destructive change from an agent (drop a column or table, delete a bucket, function or site, rotate keys) returns `pending_approval` with a plan id and does nothing until a person approves it; the console Approvals page shows the plan as a diff with Approve and Reject, matching the `Approvals` mockup; the top bar badge counts pending items; the agent can poll `list_pending_approvals`; an approved plan runs exactly once; every decision is in the audit log.
- [ ] Design it (spec): `/architect approval queue`

### 76. MCP full tool set · needs a decision
The rest of the roughly 20 task level tools.
**Done when:** the tools for auth, storage, functions, webhooks, email and ops (including `explain_error` and `get_logs`) join the v0 set; all are generated from the contract with descriptions written for models; each mutating tool supports dry run. The two site tools arrive with M8.
- [ ] Design it (spec): `/architect mcp full tool set`

### 77. MCP resources & prompts · needs a decision
Context an agent can read, and ready made prompts for common jobs.
**Done when:** the resources `orvano://project/config`, `orvano://schema`, `orvano://docs/{slug}` and `orvano://events`, and the prompts `scaffold_app`, `add_auth`, `design_schema` and `debug_request`, work in a real MCP client; `search_docs` returns useful pages.
- [ ] Design it (spec): `/architect mcp resources & prompts`

### 78. Local MCP
The same tools over stdio, for editors that run a local server.
**Done when:** `orvano mcp` serves the same tools over stdio using the CLI's stored credentials.
- [ ] Build it: `/develop local mcp`

### 79. AI console pages · needs a decision
The console area where people see and manage what agents are doing.
**Done when:** the AI area lists connected agents and tokens, shows an agent activity timeline and an MCP setup guide per editor; a "Connect your AI" card shows the setup command; everything an agent did carries its name and a "via MCP" tag.
- [ ] Design it (spec): `/architect ai console pages`

### 80. Gate: agent build test · needs a decision
The gate that closes Phase 2: an agent builds the reference app on its own.
**Done when:** a coding agent, given only the skills and the MCP server, builds a reference app with auth, a table with permissions, uploads and live updates, with no human edits; the run can be repeated. Site deploy joins this test at the v1.0 gate.
- [ ] Design it (spec): `/architect agent build test`

## Deferred
- **Console assistant**: a chat panel in the console using the same MCP tools
- **AI gateway**: a managed route to model providers
- **Evals for apps**: evaluation tools for user projects
- **Migration skills**: skills that move a project from Firebase or Supabase
- **Shared MCP endpoint**: one hosted MCP address, tied to the cloud
