# /check verify design system & console shell · FAIL

_Feature 11, spec [0015](../specs/0015-design-system-console-shell/index.md), all six build milestones, AC-1 to
AC-24. Proof commit `f54f5af` on orvano main (orvanohq/orvano#115), 2026-10-27. The checks come from each criterion,
the spec's Critical test scenarios, and the screen reader script in [`verify.md`](../specs/0015-design-system-console-shell/verify.md).
Build approach: Tracer Bullet (root `AGENTS.md`). For this feature, that means the tokens, the shell, and the primitives
have to be real and working in the gallery and the built console. Placeholder data in the gallery is the plan, not a
defect. Local runs used a worktree of `f54f5af`, Node 25.2.0, pnpm 12.12.1, Playwright 1.64.1 (Chromium, Firefox,
WebKit), and axe-core 4.14.0. The gallery ran on `vite` (port 5182), and the shell probe build ran on `vite preview`
with NET-2's header. I drove it with the built in browser and with Playwright scripts._

**FAIL: 21 of 24 criteria are met. Two fail with a real defect (AC-6, AC-9), one has a small documentation gap
(AC-1), and AC-22 is owed to a person.** Every specced surface exists: all 75 names in PRM-1 are exported from
`src/ui`, and every gallery scene renders.

Next: `/debug design system & console shell` for AC-6 and AC-9, then `/develop` (or a direct edit) for the AC-1
`design.md` gap. Then run `/check verify` again. The re-run only needs to cover AC-1, AC-6 and AC-9.

Scope: nothing ticked. `Verify it` in `docs/scope/foundations.md` stays unticked, because the verdict is FAIL.
`verify.md` is unchanged: its only steps are the screen reader script, which is owed.

## Failing

### AC-6: primary and danger buttons lose their boundary in forced colors

THM-6 asks that "control borders" stay visible in forced colors mode. The `primary` and `danger` button variants (and
`IconButton` in those variants) get their shape only from a background fill and have a 0 px border. Forced colors
replaces the fill with the canvas color, so the button turns into bare text with nothing around it. `secondary`, the
unavailable state, and inputs all keep a 1 px border. The focus ring, the current sidebar item, and the current tab
are fine, and so are `prefers-contrast: more` and reduced motion (see below).

Repro (Chromium, Playwright):

1. Run `pnpm dev` in `console/`, then emulate `forcedColors: "active"` with `colorScheme: "light"`.
2. Open `/console/gallery.html#/primitives`. Under Button, the primary and danger rows ("Create sm", "Create md",
   "Create lg") show as plain words, while the secondary row shows boxes.
3. Open `#/acme/storefront/databases/users`. "Insert row" has `border-top-width: 0px` and no visible outline.

The likely fix is a transparent border on the filled variants, so forced colors paints it. Ghost buttons are borderless
in every mode by design, so they are out of scope.

### AC-9: an ancestor crumb also says `aria-current="page"`

AC-9 says the last crumb is `aria-current="page"`. On a page three levels deep, the middle page crumb is also marked
current. The crumb renders a router `Link`, and TanStack Router marks any link to an ancestor of the current route as
active. React Aria then sets `aria-current="page"` on it. A screen reader announces two "current page" items (SRT-4
expects only "users"). In forced colors, both crumbs also get the Highlight bar meant for the current item.

Repro (built in browser, or any browser):

1. Open `/console/gallery.html#/acme/storefront/databases/users`.
2. Inspect `nav[aria-label=Breadcrumb]`. The "Databases" link has `aria-current="page"` and `data-status="active"`,
   and the "users" span has `aria-current="page"`.

Why tests miss it: `shell.test.tsx` "renders the crumbs, the last one current…" asserts only that "users" is current.
It never asserts that the other crumbs are not current. The likely fix is to give non last crumb links exact matching,
or to drop their active state.

### AC-1: `design.md` misses two of the primitives feature 11 builds

The token tables are generated and checked: `pnpm check:js` passed, and so did the tests "the committed design.md
matches theme.css" and "changing a token without regenerating makes the check fail". The gap is the "every primitive"
part:

- `useDocumentTitle` (PRM-1, Shell) is not mentioned in `console/design.md` at all.
- `SkipLink` has no component row (built on, states, keys, notes). It is only described in a prose bullet ("Skip
  link: the first focusable element…") under Shell.

Every other PRM-1 name appears in `design.md`. `Field` and `Form` appear only through the `useAppForm` paragraph, which
reads as fair coverage. This is a small documentation fix, not a behavior bug.

## Owed

- **AC-22, the screen reader runs.** Neither pair has run: pair A is VoiceOver with Safari on macOS, and pair B is NVDA
  with Firefox on Windows. A person has to run SRT-1 to SRT-12 and record the results in
  [`verify.md`](../specs/0015-design-system-console-shell/verify.md), following its "How to run it" steps. As supporting
  evidence only (not a substitute), axe found 0 WCAG 2.2 A and AA violations in Chromium, Firefox and WebKit, in both
  themes, on `#/acme/storefront`, `#/acme/storefront/databases/users`, `#/acme`, `#/account/profile`, `#/auth-layout`,
  `#/boot-error` and `#/root-error`. The accessibility tree also matched the script for SRT-1, 2, 3, 5, 6, 7, 9, 10,
  11 and 12. SRT-4 will fail until AC-9 is fixed, because of the extra "current page" crumb. Run the pairs after that
  fix.

## Spec gaps (for `/architect`, not failures)

- **AC-13 vs PRM-8.** AC-13 says the palette "follows the APG dialog plus combobox and listbox patterns". PRM-8 says
  "`CommandPalette` uses React Aria's `Autocomplete` with a `Menu` inside a `Dialog`". The build follows PRM-8: the
  input is a `combobox` whose `aria-controls` popup has `role="menu"`. AC-13 should say menu.
- **AC-12's live region list.** AC-12 names only the `Announcer`, the toast region and `Banner` as live regions.
  `InlineError` is `role="alert"`, which spec 0003 A11Y-4 allows ("inline alerts … are part of their primitives"), and
  `design.md` documents it. AC-12 should list `InlineError` too.

## Per criterion

| AC    | Result  | Evidence                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ----- | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-1  | ❌ gap  | Generated tables and check pass (`pnpm check:js` exit 0, two AC-1 tests). `useDocumentTitle` is missing from `design.md`, and `SkipLink` has no component row (above).                                                                                                                                                                                                                                                                                                                                                       |
| AC-2  | ✅      | `pnpm build` emits one CSS file (`style-CSI3jfTI.css`, `cssCodeSplit: false`). The CSS has no `bg-red-500` or `text-black`. Tests "default palette, font, text, radius, and shadow utilities compile to nothing" and "the semantic tokens exist as utilities" pass. With the shell probe build under NET-2's CSP, a MutationObserver saw no `<style>` added while I drove the account menu, theme, org switcher, palette, help, activity and `?` (Chromium and Firefox with mouse and keys, Chromium and WebKit with touch). |
| AC-3  | ✅      | `pnpm size` exits 0. Shell first load (AC-3, probe) is 218.69 KB of 230, the BDL-1 first load is 161.87 KB of 230, CSS (STY-4) is 7.99 KB of 30, and the largest JS file is `react` at 67.47 KB of 80 (35 one file entries, all within the limit). PRM-10 panels (palette, shortcut help, docs pane, side sheet, table, confirm delete) are separate chunks. The `zod/mini` lint fixture fires (21 of 21 fixtures).                                                                                                          |
| AC-4  | ✅      | Playwright `e2e/theme-boot.spec.ts`: 5 of 5 pass (saved dark before the app script, storage blocked, boot script failed, a live OS change under System, forced colors focus). In the browser, choosing Light by keyboard set `data-theme=light`, `color-scheme: light`, and `orvano:v1:theme="light"` at once.                                                                                                                                                                                                               |
| AC-5  | ✅      | `theme.contrast.test.ts` ("a low contrast token fails the check") passes, along with the gallery "passes axe in both themes and both densities" tests in three engines. My own axe pass found 0 violations, as listed under Owed.                                                                                                                                                                                                                                                                                            |
| AC-6  | ❌      | Forced colors: focus outline `auto`, current sidebar item and current tab get a 2 px solid inline start border, inputs keep 1 px. Primary and danger buttons have 0 px and disappear (above). `prefers-contrast: more` makes `--color-border` equal `--color-border-strong` and `--color-fg-muted` equal `--color-fg` in all three engines. With reduced motion, the side sheet has no transition or animation in its ancestors, and skeletons have no animation (`pulse 2s` without it).                                    |
| AC-7  | ✅      | Gallery `#/acme/storefront`. After the gallery toolbar, the first Tab in the shell lands on "Skip to content". Enter moves focus to `main#main` (`tabindex=-1`). Landmarks are `header`, `nav` "Breadcrumb", `nav` "Project" and one `main`. Overview is `aria-current="page"`. The org and account scopes show the Organization and Account navs with their own items.                                                                                                                                                      |
| AC-8  | ✅      | At 1280 px, "Collapse sidebar" gives a 56 px rail. Links keep names such as "Databases", hover shows the tooltip "Databases", and the state is saved as `orvano:v1:usr_ada:layout {"sidebarCollapsed":true}`. At 800 px, "Open navigation" opens a dialog named "Project" with focus inside. 15 Tabs stay inside, and Escape closes it with focus back on "Open navigation". Widening to 1100 px with it open closes it. At 320 px, `scrollWidth` is 320 and no element overflows.                                           |
| AC-9  | ❌      | Switcher, collapse and title all work. The project switcher opens with 4 results announced as "4 projects", focus is in "Search projects", typing "b" announces "2 projects", and it ends with "All projects" and "New project". At 800 px the middle crumbs fold into "Show the full path". The title is `users · Databases · Storefront · Orvano`. 9 AC-9 tests cover debounce, abort, the 20 limit, and the empty and error states. The current crumb marking fails (above).                                              |
| AC-10 | ✅      | Separator "Resize table list": ArrowRight changes 280 to 296, End gives 480 with 560 px of content left, and Home gives 200. The width is saved as `orvano:v1:usr_ada:storefront:svc:databases {"width":200}`. "Collapse list" gives a 40 px strip with "Expand list", saved as `collapsed:true`. A long table name is truncated visually and keeps its full text as the name. 7 AC-10 tests pass.                                                                                                                           |
| AC-11 | ✅      | The menu shows Ada Lovelace and ada@example.com, then a Theme group (System, Light, Dark, `menuitemradio` with `aria-checked`), a Density group, "Keyboard shortcuts" and "Sign out". Choosing Compact by keyboard sets `data-density=compact` and saves it, and rows on `#/data` change from 40 to 28 px. Text inputs stay 32 px. No shell target is under 24 by 24 px (the only ones are the gallery's own link and the hidden skip link).                                                                                 |
| AC-12 | ✅      | On `#/feedback`, I raised success twice, then info, then error, with focus on the buttons. The region "Notifications" sits at the bottom inline end and shows 3 toasts, newest on top, with "Table created, 2 times" merged. Focus stayed on "Error". After 7 s only "Deploy failed" was left. Activity reads "Activity, new messages", and its popover shows "No running jobs" and "Messages (4 messages)".                                                                                                                 |
| AC-13 | ✅      | ⌘K from the top bar opens dialog "Command palette" with a `combobox` input. It lists Go to (each nav item), Theme, Density, "Show keyboard shortcuts" and "Open help". Typing "data" then Enter went to `#/acme/storefront/databases`. Reopening showed "Recent: Go to Databases" first, and Escape returned focus to "Help". `?` opens "Keyboard shortcuts", which lists ⌘K and ?. ⌘K while that is open does nothing. The palette button has `aria-keyshortcuts="Meta+K"`. Note the menu vs listbox wording gap above.     |
| AC-14 | ✅      | Help sits at x=1027, y=53 in project, org and account scopes. At 1280 px, Enter opens an `aside` 400 px wide beside the content, `main` narrows to 440 px, focus goes to the "Help" heading, and the link goes to the docs address with `target=_blank` and `rel="noopener noreferrer"`. At 800 px it is a modal dialog "Help", and Escape returns focus to "Help".                                                                                                                                                          |
| AC-15 | ✅      | All 75 PRM-1 names are exported from `src/ui`. 37 AC-15 tests pass in Chromium, Firefox and WebKit (`vitest run`: 85 files, 1,043 tests), including "an unavailable button stays focusable, ignores presses, and exposes its reason (PRM-4)" and "preloads the target route on hover" and "…on keyboard focus". In the browser, Dialog "Rename table" keeps focus inside across 8 Tabs, and Escape returns focus to the opener.                                                                                              |
| AC-16 | ✅      | Submitting "New webhook" empty moved focus to the error summary ("Fix this problem to continue"), with a link to the URL field. Typing and pressing Escape opened "Discard unsaved changes?" in the design system dialog. 11 AC-16 tests pass.                                                                                                                                                                                                                                                                               |
| AC-17 | ✅      | "Load in 200 ms" shows no skeleton. "Load in 1.5 s" shows the skeleton at about 310 ms and removes it at about 1,530 ms. ConfirmDelete shows "Also deleted: 12 tables, about 48,210 rows" with type to confirm (tier 1), "Counting what will be deleted with it…", and "Couldn't count…" (tier 2). The 500 ms minimum is covered by the AC-17 timing tests (11 pass).                                                                                                                                                        |
| AC-18 | ✅      | With direction set to right to left (ar-XB), the sidebar is at x 1040 to 1280, chevrons have `scale: -1 1`, ArrowLeft on the separator widens it (200 to 216), the toast sits at the bottom left, and at 800 px the drawer is at x 561 to 800. `pnpm check:js` (literal JSX text) and the RTL-2 lint fixture pass, along with 5 AC-18 tests. The pseudo locale translated the shell ("Śķĩƥ ţō ćōńţēńţ") with no overflow.                                                                                                    |
| AC-19 | ✅      | `pnpm check:lint-fixtures`: "all 21 restriction rule IDs fire".                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| AC-20 | ✅      | The gallery is served at `/console/gallery.html` with all three scopes, every scene, and toolbar switches for theme, density, locale, direction and width. A gallery only string ("Placeholder content for the project scope") is absent from both `messages.po` catalogs. `check-gallery-not-shipped.mjs` reports "no gallery file or chunk in the build".                                                                                                                                                                  |
| AC-21 | ✅      | axe found 0 violations on `#/auth-layout`, `#/boot-error` and `#/root-error` in both themes and three engines. 3 AC-21 tests pass.                                                                                                                                                                                                                                                                                                                                                                                           |
| AC-22 | ⏳ owed | See Owed above.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| AC-23 | ✅      | `pnpm e2e`: "/ loads under the console CSP with no violation" passes. The shell probe build under the same header gave 0 `securitypolicyviolation` events in four engine and input runs.                                                                                                                                                                                                                                                                                                                                     |
| AC-24 | ✅      | On `#/data`, "Copy Project ID" copied and announced "Copied" once. With `navigator.clipboard.writeText` rejecting, the value `prj_01JB8Z4Q7W2N` is selected, "Press ⌘C to copy" shows, and nothing says "Copied". 4 AC-24 tests pass.                                                                                                                                                                                                                                                                                        |

## Commands run

All from the worktree at `f54f5af`. Every command exited 0.

- `pnpm install --frozen-lockfile`
- `pnpm --filter @orvano/console build`: typecheck, then Vite
- `node tooling/scripts/check-gallery-not-shipped.mjs`
- `pnpm --filter @orvano/console size`
- `pnpm run check:js`: design tokens, `vp check`, ESLint, typecheck, Spectral, API check, drift
- `pnpm run check:lint-fixtures`
- `pnpm --filter @orvano/console test`: 85 files, 1,043 tests, Chromium, Firefox and WebKit
- `pnpm --filter @orvano/console e2e`: 7 of 7

`check:dotnet` was not run, because this feature touches only `console/`.

## For /check review

- **Dormant runtime style injection.** The built bundle contains two React Aria paths that add a `<style>` element
  (`createElement("style")` with a nonce) in the `brand-mark` and `index` chunks. Neither fired in any flow I drove.
  NET-2 is `style-src 'self'` with no nonce, though, so if either fires in production it is blocked and logged as a
  violation. It's worth finding what triggers them, and whether AC-2's "no code injects" holds once feature 13 adds
  real screens.
- **Breadcrumbs at 320 px.** There is no horizontal scroll, but the org crumb shrinks to 19 px (only its chevron
  shows) and the current crumb to 13 px ("O."). It's readable by name, but hard to read visually.
- **Truncation in right to left.** A long Latin table name truncates at the start in right to left
  ("…ng_name_that_truncates").
- **Switcher Escape.** The first Escape in a switcher only clears the search text, and a second one closes it. That is
  React Aria SearchField behavior and fits APG, but SRT-4 testers should expect it.
