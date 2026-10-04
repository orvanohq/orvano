# App verification program landscape

The five review programs behind SKILL.md step 3's tier ladder, and the comparison table. All figures from the named vendor's own documentation unless labeled otherwise.

## Comparison

| Program                      | Trigger                                 | Requirement                                                 | Timeline                                                        | Recurrence             |
| ---------------------------- | --------------------------------------- | ----------------------------------------------------------- | --------------------------------------------------------------- | ---------------------- |
| Google CASA                  | Restricted scopes                       | Tier 2 DAST / Tier 3 pentest (OWASP ASVS-based)             | ~10 days (sensitive tier) + assessment months (restricted)      | Annual                 |
| Microsoft M365 Certification | Optional / tenant-required              | Independent audit mapped to SOC 2 / PCI DSS / ISO 27001     | Not published                                                   | Yearly                 |
| Salesforce Security Review   | AppExchange listing                     | OWASP + cross-org isolation + OAuth token handling          | 1-2 wk verification + 3-4 wk testing; each resubmission +2-3 wk | On new versions        |
| Shopify                      | Public app with protected customer data | Data-minimization approval + mandatory GDPR webhooks        | Varies                                                          | Per review             |
| Atlassian Cloud Fortified    | Enterprise trust badge                  | Continuous Ecoscanner + Bug Bounty + annual self-assessment | Ongoing                                                         | Annual self-assessment |

The convergent design across all five: **review depth scales to data sensitivity** - self-serve or attestation for low-sensitivity scopes, mandatory independent testing once an app can reach bulk or other-users' data.

## Google - the tiered-scope archetype

- Three scope tiers:
  - **non-sensitive**: basic profile, no review.
  - **sensitive**: Gmail send, Drive file access, Calendar..., Google's own verification, ~10 days after a complete submission.
  - **restricted**: full Gmail/Drive, third-party CASA assessment _before_ Google's review begins, pushing total time to months.
- **CASA** (Cloud Application Security Assessment, run by the App Defense Alliance on OWASP ASVS) has three tiers: Tier 1 self-assessment, Tier 2 third-party DAST scan (the common landing tier), Tier 3 full manual pentest. Reassessment happens every 12 months, and adding a restricted scope can trigger an out-of-cycle one.
- **The developer pays the assessor, not Google**: Tier 2 around $500+ (community-reported discounted rate with assessor TAC Security), Tier 3 $5,000+ (community-reported). The cost, not just the calendar, makes restricted scopes a genuine go/no-go for small developers.
- **The unverified lane**: "Testing" status allows named test users without review. It ships with a tester warning screen, a **100-user lifetime cap** that only completing verification lifts (current Google docs; older cached sources say 50 - the figure moved once already), and **7-day** consent/refresh-token expiry for test users.
- **The enforcement trigger**: the unverified-app warning fires when runtime-requested scopes diverge from the declared, approved consent-screen configuration - the model for enforcing "runtime request ⊆ approved declaration" rather than trusting declarations to stay accurate.
- The warning copy, as a reference for designing an equivalent: "Google hasn't verified this app" / "...you shouldn't use it", with the click-through reading "Go to [App] (unsafe)". Apps requesting only basic profile skip the warning, the cap, and the short expiry entirely - the apparatus keys on data sensitivity, not on verification status per se.

## Microsoft - three signals, kept deliberately distinct

1. **Publisher Verification** - the blue consent-prompt badge. Verified partner (CPP) account, work/school Entra registration, verified non-`onmicrosoft.com` domain, MFA; no fee; "can be verified in minutes" for a prepared publisher. It measures identity continuity only - Microsoft states the badge "doesn't imply or indicate quality criteria".
2. **Publisher Attestation** - self-assessment; "Microsoft does not independently verify the information submitted" and "most attestations can be completed in one hour or less".
3. **M365 Certification** - yearly independent third-party audit including a pentest, mapped to SOC 2 / PCI DSS / ISO 27001.

**The real enforcement lever is consent blocking, not the badge**: since November 2020, with risk-based step-up consent enabled, users can't consent to most newly registered multitenant apps that aren't publisher-verified when the app requests permissions beyond basic sign-in/profile (Microsoft Learn). Copy both lessons: keep identity/attestation/audit legible as three separate signals, and make the bottom tier a consent gate, not a decoration.

## Salesforce - the review, and the breach that hardened it

- The Security Review (required for AppExchange/AgentExchange listing) tests OWASP classes plus cross-org data isolation and OAuth token handling - "token leakage, improper refresh token handling, authentication bypass" (Salesforce partner guidance). Timeline per Salesforce: 1-2 weeks verification, 3-4 weeks testing, +2-3 weeks per resubmission.
- **The incident context**: the August 8-18, 2025 Salesloft Drift OAuth-token campaign (actor UNC6395) hit "more than 700 organizations" (Google Threat Intelligence Group/Mandiant); the ShinyHunters actor separately _claimed_ ~1.5 billion Salesforce records from 760 companies via OAuth-token abuse (actor claim, not a verified count).
- **The response - what a forced retrofit looks like**: four mandatory OAuth controls including PKCE and refresh-token rotation on all Connected Apps and External Client Apps, enforced May 11, 2026, de-listing/suspension as penalty. Plus a September 2025 shift from trust-by-default (uninstalled connected apps now need explicit permission to authorize; device flow removed). Salesforce ISV practitioners published detailed retrofit accounts - the concrete cost a 2.1-compliant day-one build avoids.

## Shopify - data minimization as the organizing principle

Since API version 2022-10, Shopify redacts customer personal data by default. Apps apply for protected-data access, and approval is scoped to "the minimum amount required" for the stated purpose. GDPR webhooks (`customers/redact`, `shop/redact`, `customers/data_request`) are mandatory for every listed app regardless of whether it stores customer data.

Documented rejection reasons worth turning into your own checklist:

- broken functionality
- excessive permission requests relative to stated functionality
- misleading listing copy
- non-compliant billing
- storefront performance impact

## Atlassian - continuous scanning, earned badge

- **Ecoscanner** scans every Marketplace cloud app continuously, not once at submission.
- **Cloud Fortified** badge stacks process on top: continuous scan passing, a Vulnerability Disclosure Program, bug-fix SLAs, a Bugcrowd bug-bounty, annual security self-assessment.
- **Partner Verification** gates listing for partners whose first app listed on or after June 2, 2025, rolling, not retroactive.

The lesson: point-in-time review and continuous posture are different products. A marketplace at scale needs both.

## Design implications

1. Tier by data sensitivity, with the bright line at scopes reaching **other users'** data or PII - that's where every program above places mandatory independent testing in some form.
2. Recurring, never one-shot: annual reassessment (Google, Microsoft, Atlassian) plus re-review on new versions (Salesforce).
3. Build the recurring rejection themes into the review checklist: over-broad scope requests and weak OAuth token handling recur across Salesforce, Shopify, and Atlassian's stated rejection reasons.
4. The unverified lane needs a real, enforced ceiling (Google's lifetime cap) - a warning banner alone doesn't change developer behavior.
