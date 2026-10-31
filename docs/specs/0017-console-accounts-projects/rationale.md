# 0017. Console accounts and projects: rationale

Decision record for [index.md](index.md). `/develop` doesn't need this file.

## Context

> ⚠️ Premise note: the scope line says an operator "signs up", but on a self hosted install an open sign up page lets a stranger join the instance, and no rate limit engine or email exists yet to protect it. The real job is "the person who installed Orvano gets in", which the setup token already gates. The spec narrows to that, and the engineer agreed. Spec 0006 CON-4 also says operator TOTP ships in the first release; this feature leaves it to feature 62 on purpose, so that gap is visible and enrolled, not silent.

Feature 12 stops at a checked setup token, and everything after it needs an account: the console shell has no session, project routes have no project, and the SDK conformance suite has no way to get a key. Feature 13 is where the shared rules of specs 0006, 0011, and 0012 first meet real tables and routes, so any hole in them shows here.

Forces that shaped the choices:

- Orvano will hold other people's secrets. Operator accounts are the top target in spec 0006's threat model, because one takeover reaches every project.
- Features that would normally protect this feature come later: rate limits (23), email (17), audit storage (48), usage (45), and teams (51). Each gap needs an honest stand in or a stated limit.
- The build approach is Tracer Bullet, but this feature is mostly control plane, which SDKs never call. Something has to carry the thread to them.
- The user wants to reach the core loop soon, so breadth beyond the Done when line has to earn its place.
- Spec text must stay trustworthy. `/sync` had to flag six `Updated` lines whose rule text was stale, so this spec changes rule text directly.

Workspace: the core monorepo, with changes in `server/`, `console/`, `api/`, and small runner changes in six SDK repos and three templates. Compliance: operator personal data only, under spec 0006's ASVS 5.0 Level 2 bar.

## Options considered

### Option 1: Owner only accounts in the Platform module, server side sessions, hashed keys

Build what specs 0006, 0011, and 0012 describe, in `Orvano.Modules.Platform`, with one owner made by the setup form.

**Pros**: reuses what's already designed and tested (the access layer, CAT-1, the hasher); a revoke or sign out is immediate; no new dependency.

**Cons**: Orvano owns its session and password code; several stand ins until later features arrive.

### Option 2: ASP.NET Core Identity for operators

Use Identity's user store, password handling, and cookies.

**Pros**: a known library with much of this already written.

**Cons**: its schema doesn't match CAT-1's conventions (uuid v7, naming, data classes), its hasher isn't Argon2id without a replacement, and its cookie and lockout model differs from CON-1 and PWD-5 (spec 0006 forbids hard locks). Most of it would be overridden.

### Option 3: Operators as end users in a reserved project

Make operators rows in `auth.user` under a system project, sharing feature 14's code.

**Pros**: one identity system, and features 14 and 18 would serve operators too.

**Cons**: end users are per project by design, and feature 14 doesn't exist yet. It ties the console's security to a data plane, breaks spec 0011's split between `operator` and `user`, and turns a bootstrap problem into a chicken and egg one.

## Rationale

Option 1 wins because the hard design work is already in specs 0006, 0011, and 0012, and the access layer waits for exactly these resolvers. Identity would fight those rules, and Option 3 would make the riskiest accounts depend on a later feature. Keeping session state on the server is what makes sign out, revoke, and expiry immediate, which matters more here than saving one indexed read per request.

### Decisions the engineer made

| Decision     | Choice                                                           | Why it wins                                                                                            | Runner up                             |
| ------------ | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ | ------------------------------------- |
| Sign up      | Setup owner only                                                 | No stranger can join; a second account path isn't worth securing before feature 23 and feature 17      | Optional open sign up, off by default |
| First org    | One form, signed in after                                        | One transaction, no state with an operator and no organization                                         | An automatic default organization     |
| Operator MFA | Own feature (62), before alpha                                   | Not in Done when; pulls in crypto tables, a second sign in step, and recovery codes                    | All of CON-4 here                     |
| Lifecycle    | Delete projects, not organizations                               | Teardown is needed anyway for a failed create, and test projects must be clearable                     | Delete both                           |
| SDK strand   | `keys.current`                                                   | Proves the key resolver, KEY-6, and PRJ-1 from every SDK now, so feature 14 starts on a running thread | Bootstrap only, SDKs unchanged        |
| First keys   | None made for you                                                | Nothing secret exists until asked for, and only one place reveals a key                                | Auto create a publishable key         |
| Key form     | Pick scopes, optional expiry, fixed after creation               | Least privilege by default, and a key's power never shifts under a running app                         | Editable scopes                       |
| `ar`         | A real catalog                                                   | Proves Arabic text, not only mirrored layout, and settles FRS-7                                        | RTL-1's proof only                    |
| Audit        | `IAuditSink`, logs for now                                       | The event shape is fixed at the call sites now, and feature 48 swaps the sink                          | Building `audit_event` now            |
| Throttle     | In memory, per process                                           | Same shape as STP-8, and one replica is the reference install                                          | Counted in the database               |
| Reset        | One time link                                                    | No password passes through a terminal, and the page serves a later email reset                         | Printing a generated password         |
| Bootstrap    | Unattended setup command                                         | Same image as production, and it doubles as automated install                                          | A test only command                   |
| Data model   | CAT-1, less TOTP, plus `locale`, `last4`, `operator_reset_token` | Every addition answers a rule that had no source                                                       | Keeping unused TOTP columns           |
| Account page | Name, email, language, password                                  | A mistyped owner email must be fixable, and `reset-operator` looks up by email                         | Language and password only            |

### Decisions made while writing the spec

| Decision                                           | Choice and why                                                                                                                           | Runner up                                                               |
| -------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| Routes with no organization and routes for any key | Two new requirement kinds. Spec 0012's three kinds can't express either, and a public route runs with no caller (EPT-5)                  | One operation per key type, which doubles every SDK method              |
| `session_expired`                                  | `Failed` may carry the code. QRY-8 needs it, feature 14 will too, and the status stays `401`                                             | Treat every console `401` as expired, which breaks boot's signed out    |
| CSRF check                                         | Keyed on the caller, not metadata, so a new endpoint can't forget it                                                                     | Endpoint metadata, as `RequireConsoleOrigin` does                       |
| Module references                                  | Platform uses Auth's contracts, and `IProjectDirectory` moves to SharedKernel. Both directions would be a project cycle                  | Moving `IPasswordHasher` to SharedKernel, which empties Auth's contract |
| Key rows                                           | Never cached. KEY-3 says the next request, and one indexed read is cheap                                                                 | HybridCache with a signal, which adds a 60 second failure window        |
| Key project status                                 | Read in the key's own query, so a delete stops keys on the next request. The directory cache serves CORS, where 60 seconds is acceptable | `IProjectDirectory` for both, which can lag until the signal lands      |
| Owner lockout through the throttle                 | Keep PWD-5's per account limit, and let a reset clear the entry. Spec 0006 forbids a weaker limit (STD-2)                                | Counting per account and address, which relaxes PWD-5                   |
| Key rename                                         | No `If-Match`: `last_used_at` moves `xmin` every minute, so versions would fail nearly every rename                                      | Moving last used to its own table                                       |
| Secret key plus bearer token                       | `401`, not KEY-2's `400`: PRJ-4 and RSV-3 say a failed mix is a failed credential, and publishable plus bearer is valid                  | Keep `400` as a special case in one resolver                            |
| `last4`                                            | A column, because UX-33 and `SecretReveal` need it. 24 bits of 256 reveal nothing useful                                                 | Prefix only, which edits a practice spec                                |
| Regenerate                                         | Built, because UX-33 names it and KEY-3 asks for rotation; it's one transaction                                                          | Leave it to create and revoke by hand                                   |
| Regenerate overlap                                 | The old key stops at once, and the confirm says so. Create plus revoke gives overlap to anyone who wants it                              | A grace period, which adds a second live hash                           |
| Stuck `creating` project                           | The hourly job sweeps it after an hour, since a crash can land between TXN-1's steps                                                     | Leaving the row, hidden from the data plane forever                     |
| `services` and capabilities                        | Fields on `ProjectResponse`, no separate call. RTE-9 only needs it in route context                                                      | A capabilities endpoint                                                 |
| `features` in config                               | A string array, empty today. CPX-3 fixes the name, and an empty list costs nothing                                                       | Leave it out until a switch exists, which changes the contract later    |
| Common password list                               | 10,000 entries, embedded. PWD-3 forbids an outside call by default, and it stays small in memory                                         | A larger list, at more memory for little gain                           |
| Meter on `keys.current`                            | `platform.keys.read`. It's a data plane call, so it's countable from day one                                                             | No meter, as health has none                                            |
| Migrations                                         | One for the thread and one for the reset token, sized to the milestones                                                                  | One per milestone, which adds churn for no safety                       |

### Spec text changed with this feature

Commit 1 of the PR fixes what `/sync` flagged. Commit 2 holds this spec, its amendments below, and the scope.

- **Spec 0003**: I18N-3, because `ar` must now stay complete.
- **Spec 0006**: KEY-2's stored fields and mixed credential answer; CON-2's pre session exceptions; CON-4's timing; CON-6's reset.
- **Spec 0011**: CAT-1 per Data model sketch; ENC-3; LIF-4; FLT-1; RET-1; the feature 13 follow up.
- **Spec 0012**: CPL-6 (new), EPT-2, EPT-3, EPT-5, SCP-5, RSV-2, AC-13, and the API table, per ACX-1 to ACX-3 and SES-8.
- **Spec 0014**: PTH-1's services, PRB-8's `session_expired`, XTN-1's audience, XTN-3's implied codes, CPX-3's payloads, CNF-7's command name, and CNC-1's exception for API keys.
- **Spec 0015**: RTL-1, since the real catalog now exists.

Spec 0016's Follow-up for feature 13 stays open until the code lands.

### Cross check (2026-10-30)

A read only pass on another model found the core design sound and raised 19 gaps: undeclared rule changes, values with no source, and two promises the design couldn't keep (a next request delete behind a cache, and an SDK proof before merge). The engineer took every recommended fix, and they're in index.md. One finding was dropped: spec 0011's state transitions already allow `creating` to `deleting`.
