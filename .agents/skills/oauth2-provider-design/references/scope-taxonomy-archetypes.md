# Scope taxonomy archetypes

The named provider models behind SKILL.md step 2's granularity menu. All figures and quotes from the named provider's own documentation or blog.

## Cross-provider comparison

| Provider                    | Scope pattern                               | Read/write split           | Admin consent tier                       | Notable                                     |
| --------------------------- | ------------------------------------------- | -------------------------- | ---------------------------------------- | ------------------------------------------- |
| GitHub (Apps)               | Fine-grained per-resource permissions (50+) | Yes, per-resource          | Org/enterprise approval policies         | Installation-scoped, 1h tokens              |
| GitHub (classic OAuth Apps) | Broad scopes (`repo`, `user`)               | No - `repo` = full control | Org can restrict                         | Legacy; Apps preferred                      |
| Slack                       | `resource:action`                           | Yes                        | Admin-approved apps                      | Bot vs. user token scopes                   |
| Microsoft Graph             | `Resource.Action.Scope` (`User.Read.All`)   | Yes                        | Delegated vs. application; admin consent | Unified across M365                         |
| Stripe Connect              | `read_only` / `read_write`                  | Coarse, 2 levels           | N/A (platform model)                     | OAuth deprecated for new Standard platforms |
| Google                      | URL scopes; sensitive/restricted tiers      | Yes (`.readonly`)          | Workspace domain-wide delegation         | Incremental auth supported                  |

## GitHub - the canonical broad-vs-granular case study

- Classic scopes are the anti-pattern in the vendor's own words: classic tokens "are given permissions from a broad set of read and write scopes. They have access to all of the repositories and organizations that the user could access, and are allowed to live forever" (GitHub docs). The fine-grained-PAT launch post (October 2022) is unusually candid, framing coarse scopes plus immortal tokens as a leading contributor to real breaches.
- The replacement: 50+ granular permissions, each independently no-access/read/read-write, per-repository restriction, org-owner approval policies (GitHub docs). GitHub's stated preference: "GitHub Apps are preferred to OAuth apps because they use fine-grained permissions, give more control over which repositories the app can access, and use short-lived tokens."
- **The migration is documented as lossy**: fine-grained tokens "cannot accomplish every task" classic ones can - confirmed gaps include contributing to public repos where the user isn't a member, and outside-collaborator repos (GitHub docs). This is why classic stays "fully supported" rather than removed: granular models lag broad ones in edge-case coverage throughout the transition. Plan the same lag into your own migration.
- **Lifetime is coupled to granularity, not independent**: installation tokens expire in 1 hour; classic PATs live forever. Fine-grained permissions and short lifetimes shipped as one security upgrade.
- Rate-limit nuance, commonly mis-cited: default 5,000 requests/hour; the 15,000/hour tier applies only to Enterprise Cloud-owned/approved apps; non-Enterprise installations scale from 5,000 by +50/hour per repo or user to a 12,500 cap (GitHub docs).

## Slack - per-resource-type scopes and token-type binding

- Scopes split by conversation type, not action alone: `channels:history`, `groups:history`, `im:history`, `mpim:history` - four scopes where a coarse model has one `messages:read`. The purchase: a DM grant that doesn't silently include private channels. Scope explosion is granularity's price; pay it where resource types genuinely differ in sensitivity.
- **Every scope belongs to exactly one token type**, and the same name means different things per type: `chat:write` on a bot token (`xoxb-`) posts as the app; on a user token (`xoxp-`) it posts as the authorizing human. Slack's migration guidance: "Migrating from user tokens to bot tokens requires reauthorization from every user" - the token-type binding decision is load-bearing for every future migration; make it per-scope on day one.
- Admin governance sits above scopes: workspace admins can require pre-install approval. Slack's stated operating principle: "Asking for more permissions than necessary can cause users or admins to reject the installation" - over-scoping fails at a consent surface the end user cannot approve for you.

## Microsoft Graph - delegated vs. application permissions

Two structurally different permission types, not two tiers of one thing (Microsoft Learn):

- **Delegated** - the app acts on behalf of the signed-in user and can never exceed what that user could do.
- **Application** - app-only identity ("app roles"), independent of any signed-in user; only a Privileged Role Administrator or Global Administrator can consent. Many high-privilege delegated permissions also require admin consent.

This is Slack's bot-vs-user split expressed as a field on the permission itself rather than the token. Either encoding works; not choosing one is the failure.

## Design rules distilled

1. Default to `resource:action` with read/write split and read-only defaults - the dominant idiom (Slack, Microsoft Graph, Stripe).
2. Scopes are additive-only, forever. Repurposing a scope silently rewrites what past consents authorize, and scope-set changes invalidate refresh tokens and force re-consent (Google docs).
3. Bind scope to token type (app identity vs. user delegation) from the first scope shipped, if both exist.
4. Gate high-privilege and org-wide scopes on admin consent - the Microsoft-proven pattern, and the second consent surface every B2B provider needs.
5. Keep the coarse tier alive while a fine-grained tier matures - GitHub's documented edge-case gaps are the expected shape of that transition, not a GitHub-specific accident.
