# /check verify design system & console shell (re-verify) · PASS

_Feature 11, spec [0015](../specs/0015-design-system-console-shell/index.md). A short follow up to the
[first verify](2026-10-27-verify-design-system-console-shell.md), which met 21 of 24 criteria. This run covers only the
three it failed (AC-1, AC-6, AC-9), plus AC-3 as a quick regression check. Proof commit `e3aaf3f` on orvano main
(orvanohq/orvano#117), 2026-10-27. Local runs used a worktree of `e3aaf3f`, Node 25.2.0, pnpm 12.12.1, and Playwright
1.64.1 (Chromium, Firefox, WebKit). The gallery ran on `vite` (port 5183), and I drove it with Playwright scripts._

**PASS for feature 11: with both reports together, 23 of 24 criteria are met, and AC-22 (the screen reader runs) is
owed to a person.** AC-1, AC-6 and AC-9 now pass, and AC-3 still passes. The other 20 criteria were met in the first
report and were not run again.

Next: `/test design system & console shell`. AC-22 still has to be run by a person before the feature is `done`.

Scope: ticked `Verify it` in `docs/scope/foundations.md`, with a note that AC-22 is owed. `verify.md` is unchanged,
because its only steps are the screen reader script, which nobody has run yet. The spec status stays `In Progress`.

## Owed

- **AC-22, the screen reader runs.** Neither pair has run yet: pair A is VoiceOver with Safari on macOS, and pair B is
  NVDA with Firefox on Windows. A person has to run SRT-1 to SRT-12 and record the results in
  [`verify.md`](../specs/0015-design-system-console-shell/verify.md). The AC-9 fix unblocks SRT-4: the breadcrumb now
  has one "current page" item ("users"), as the script expects.

## Per criterion

| AC   | Result          | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| ---- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-1 | ✅              | `console/design.md` line 323 has a `SkipLink` row (built on `a` to `#main`, hidden until focused, Tab and Enter, notes). Line 327 has a `useDocumentTitle` row. Both match the code. `src/ui/shell/skip-link.tsx` is an `a href="#main"` that moves focus to `main`, and it is `sr-only` until focused. `src/ui/shell/use-document-title.ts` leaves the org out and joins the crumbs with `Orvano`. Every other PRM-1 component name appears in `design.md` (`Field` and `Form` through `useAppForm`, as before). `node tooling/scripts/design-tokens.mjs --check` printed "design.md's token tables match theme.css."                                                                                                                                                                                                                                                                                                                                                           |
| AC-6 | ✅              | Playwright with `forcedColors: "active"` on `#/primitives`, in light and dark. In Chromium and Firefox, all 10 filled buttons (primary and danger, "Create sm/md/lg", "Unavailable", "Disabled") have a `1px solid` border painted in the system color, and so does "Insert row" on `#/acme/storefront/databases/users`. The screenshot shows the primary and danger rows boxed like the secondary row. Ghost stays borderless, which `design.md` allows. The current sidebar item keeps its 2 px inline start border. `IconButton` uses the same variant classes in `button.tsx`, and the gallery shows it only as ghost and secondary. In WebKit, the emulation turns on the media query but the engine does not force colors, so the border stays the authored transparent one. That matches Safari, which has no forced colors mode, so it is not a failure. The new test "in forced colors mode every bordered variant keeps a visible border" passes in all three engines. |
| AC-9 | ✅              | `nav[aria-label="Breadcrumb"]` in Chromium, Firefox and WebKit has exactly one `[aria-current]` element, and it is the last crumb, on three routes: `#/acme/storefront/databases/users` ("users"), `#/acme/storefront/databases` ("Databases"), and `#/acme/storefront` ("Overview"). "Databases" on the users route no longer has `aria-current`. Nav styling still works: the current sidebar item "Databases" (and "users" in the Tables nav) still has `aria-current="page"` and the `accent-subtle` background, while "Overview" has none. In forced colors, only "users" gets the 2 px Highlight bar now. The new test "on a nested route only the last crumb is current" passes in all three engines.                                                                                                                                                                                                                                                                     |
| AC-3 | ✅ (regression) | `pnpm build && pnpm size` exits 0. "Shell first load (AC-3, probe)" is 218.72 KB of 230, "First load (BDL-1)" is 161.88 KB of 230, and "CSS (STY-4)" is 7.99 KB of 30. Every file is within its 80 KB limit.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |

`pnpm exec vitest run src/ui/button.test.tsx src/gallery/shell.test.tsx -t "AC-6|AC-9"` gave 18 passed (6 files over
the three engines).

## Still open from the first report (for `/architect`, not failures)

- AC-13 says listbox, but PRM-8 and the build use a Menu.
- AC-12's live region list leaves out `InlineError`.
