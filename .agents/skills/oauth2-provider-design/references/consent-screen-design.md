# Consent screen design

The mechanics behind SKILL.md step 2's consent rules. Sources named inline; the evidence-quality warning at the end applies to the whole topic.

## Plain-language scope mapping

Every provider studied renders scopes as human sentences, never raw identifiers - Google "lists each permission scope being requested in plain language": `crm.leads.read` becomes "View your sales leads". The tension is real and unresolved in the literature:

- Language too technical scares users into bouncing.
- Language too vague is a security problem.

The only concrete lever the field agrees on is **per-scope specificity** - Auth0 recommends individual per-scope descriptions over grouped/bucketed permission names.

- Write the consent copy for each scope when the scope is created.
- Review it like UI copy.
- Treat a scope that can't be explained in one plain sentence as a scope-design smell.

## Partial and granular grants - the implementation trap

Google's granular-permissions model lets the user grant some requested scopes and deny others in one flow, and Google runs an automated check reporting whether an app handles partial grants correctly (Google docs).

**The trap**: the scope set in the authorization _response_ may differ from the _request_, even when the user appears to approve everything. Both sides of the ecosystem must handle it:

- The provider must return the actually-granted set explicitly and support apps re-requesting denied scopes later without penalty.
- Every integrating client must diff granted-vs-requested and disable dependent features on the difference.

This is a correctness requirement, not an edge case - a provider that supports partial consent without surfacing the diff ships a silent capability mismatch into every app built against it.

## Incremental authorization - and its documented failure mode

- The intended pattern (Google's stated best practice, formalized by William Denniss's IETF draft `draft-ietf-oauth-incremental-authz`): request scopes "incrementally, at the time access is required, rather than up front" - don't request Calendar access until the user clicks "Add to Calendar". The draft names what upfront requesting produces: "over-scoped authorization and a sub-optimal end-user consent experience."
- **The war story**: GMass (Ajay Goel, practitioner blog) documents Google's own implementation re-displaying **all previously granted scopes** alongside the new incremental request (the `include_granted_scopes=true` behavior) - confusing users who already granted them and making the developer "look sloppy". Spec-compliant incremental auth with a bad re-display is still a bad consent experience.
- **The rule for your implementation**: the re-consent screen shows only the net-new scope. Treat "does the screen cleanly show the delta" as a first-class UX requirement, tested explicitly - not an assumed byproduct of protocol support.

## Re-consent triggers

Refresh tokens are invalidated when the underlying consent configuration changes (Google docs) - adding scopes to an authorized app forces the user back through consent regardless of how the request is framed. Two consequences:

- Batch scope additions where possible; every addition is a re-consent event with drop-off risk.
- Never repurpose a scope to avoid a re-consent - that silently changes what the standing consent authorizes, trading a UX cost for a security hole.

## RAR and PAR - transaction-level consent

For finance/health-grade consent:

- **Rich Authorization Requests** (`authorization_details`): structured data the consent screen renders directly - "transfer $500 to account ending 4321" instead of a generic payment scope.
- **Pushed Authorization Requests** (PAR, RFC 9126): the authorization payload goes server-to-server before redirect, keeping it out of browser URLs, logs, and history.

Neither is warranted for a typical B2B SaaS consent flow; both are the named mechanism when sensitivity crosses into per-transaction authorization.

## Evidence-quality warning

There is no rigorous, public A/B study of consent-screen conversion from a major identity provider. Circulating figures (e.g. "50% drop-off on over-scoped consent screens") trace to vendor blog posts, not measurements.

Use them, if at all, as illustrative talking points explicitly labeled as vendor claims - never as benchmarks to design against, and never in a design doc as measured fact. The defensible arguments for plain language, minimalism, and incremental consent are security and UX-design grounds, plus the B2B admin-rejection gate (Slack's own guidance) - those stand without invented numbers.
