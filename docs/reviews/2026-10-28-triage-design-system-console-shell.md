# Triage, design system and console shell (spec 0015), 2026-10-28

A planning document for feature 11's open review items: no code or spec changes here. It sorts every item from the two reviews into parallel `/develop` batches, `/architect` questions, deferrals and won't fix, so the next step can start one fix session per batch.

## Inputs and method

- [2026-10-27-review-design-system-console-shell.md](2026-10-27-review-design-system-console-shell.md): minors 6 to 33 and nits 34 to 46, cited here as **R-n**. Its five majors (R-1 to R-5) are fixed by #121, #122 and #123 and are left out.
- [2026-10-28-gallery-review.md](2026-10-28-gallery-review.md): G-1 to G-30, plus its list of suggested spec and `design.md` changes.
- Known spec issues from earlier passes, cited as **K-n**: K-1 AC-13 says listbox but PRM-8 says menu; K-2 AC-12's live regions leave out `InlineError`; K-3 the review's fifth live region (R-11, and R-20 asks for one more); K-4 the stale lines in specs 0001 and 0003 that `/sync` flagged.
- Every item was checked against `main` at `adf7db8` (file and line below are on that commit). Context read: spec 0015 (`index.md`, `verify.md`), `console/design.md`, `console/AGENTS.md`, `console/src/ui/AGENTS.md`, and `### 11.` in `docs/scope/foundations.md`. Nothing was run; the checks are code reads.

## Summary

| Class                              | Count | Items                                                           |
| ---------------------------------- | ----- | --------------------------------------------------------------- |
| Already fixed                      | 1     | R-6                                                             |
| Ready for `/develop` (batches 1-6) | 62    | 39 review items, 23 gallery items                               |
| Needs `/architect` first           | 9     | R-26, G-5, G-8, G-27, G-29, K-1, K-2, K-3, K-4                  |
| Deferred to a later feature        | 2     | G-19, G-25                                                      |
| Won't fix                          | 1     | G-30                                                            |
| Total                              | 75    | 28 minors + 13 nits (41), 30 gallery items, 4 known spec issues |

Some ready items have a part that waits for a question or a later feature; the item's row says which part. Batches 1 and 4 build to the recommended answers of Q1 and Q2; if `/architect` answers differently, only the files named in that question change.

### Already fixed

| ID  | Evidence                                                                                                                                       |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| R-6 | #123: `console/src/ui/shell/switcher.tsx:57,70` adds one message per kind (`Switch project: ${name}`), and line 91 uses `strings.trigger(...)` |

The `in` bug the review listed as already known (`confirm-delete/dependents.ts`) is fixed by #121 (`Object.hasOwn`, line 47). R-40, the raw fallback text in the same function, is still open (line 49) and is in batch 3.

### Duplicates and overlaps

There are no exact duplicates between the reviews: the gallery review already left out what the code review found. These pairs touch the same thing and are kept together:

- R-11 and K-3 are one item: the switcher's error needs a live announcement, and adding `role="alert"` would be the fifth live region. Q2 settles both, and R-20's `ErrorState` announcement with them.
- G-18 and R-15 are both the `Table` scroll container (scrollbar padding and region name; sticky header scroll padding): both in batch 5.
- G-17 and R-33 are both `ExternalLink` (target height; `href` protocol): both in batch 5.
- G-19 and R-38 are both toast announcements: R-38 (repeat is silent) is a code fix in batch 4; G-19 (double or assertive announcement) needs the screen reader run first.
- G-24 and R-26 are both heading levels and sizes: R-26 is Q5; G-24 is gallery markup in batch 2.
- G-15 and G-25 are both about the tab order before `main`: G-15 is the gallery toolbar (batch 2), G-25 is the shell's lists (deferred).

## Batches

Six batches, each for its own `/develop` session. No two batches edit the same source file. The shared generated files are the Lingui catalogs (`console/src/locales/*/messages.po`): every batch that changes strings runs `pnpm extract`, and on a rebase conflict runs it again rather than merging by hand (`console/AGENTS.md`). No batch edits `console/design.md` or a spec: the lines they need are collected in Q13 for one docs pass, so the batches stay apart. Each PR must be up to date with `main`; merge order does not matter.

| Batch                               | Size | High impact | Items                                                                                      |
| ----------------------------------- | ---- | ----------- | ------------------------------------------------------------------------------------------ |
| 1. Shell top bar, crumbs, switchers | L    | G-1         | G-1, G-9, G-26, R-9, R-10 (switcher), R-11, R-39, R-41, R-44, R-45                         |
| 2. Gallery and pseudo locale        | L    | G-2         | G-2, G-3, G-4, G-6, G-10, G-11, G-12, G-13, G-14, G-15, G-16, G-21, G-22 (a-c), G-24, G-28 |
| 3. Overlays, menus, palette         | M    |             | R-7, R-8, R-10 (`ConfirmDelete`), R-12, R-22, R-25, R-40, G-20, G-23                       |
| 4. Forms, feedback, shortcuts       | M    |             | R-18, R-20, R-21, R-23, R-38, R-43, R-46, G-7, G-22 (d, e)                                 |
| 5. Data display, lists, storage     | M    |             | R-13, R-14, R-15, R-16, R-17, R-19, R-31, R-33, R-34, R-35, R-36, R-37, R-42, G-17, G-18   |
| 6. Theme, lint, CI tooling          | M    |             | R-24, R-27, R-28, R-29, R-30, R-32                                                         |

Start batches 1 and 2 first: they hold the two high impact items. Run G-1 with G-2's padded locale on once both are merged (the gallery review's note): the follow-up check is in batch 7.

### Batch 1. Shell top bar, breadcrumbs and switchers (L)

| ID              | Fix                                                                                                                                                                                                      | Evidence on `main`                                                                                    |
| --------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| G-1             | Per Q1: below 25rem the crumbs show only the ellipsis menu and the current page; every crumb control has a 24 by 24 px minimum target; the last crumb keeps its full name as accessible name and tooltip | `breadcrumbs.tsx` collapses only below 64rem; switcher trigger `max-w-48 min-w-0` (`switcher.tsx:93`) |
| G-9             | Mark the current org or project in the switcher list (check icon, `aria-current="true"`) and start the highlight on it                                                                                   | `switcher.tsx` has no current marker (only the prop comment, line 33)                                 |
| G-26            | A close `IconButton` at the drawer's inline end, as the docs pane has                                                                                                                                    | `shell/drawer.tsx` has no close control                                                               |
| R-9             | Preload the lazy palette, shortcut help and docs pane on idle and on `pointerenter`/`focus` of their buttons; a busy state after 300 ms (`useDelayedPending`). Not the hidden input (see won't fix)      | `app-shell.tsx:26-34` `lazy`, `Suspense fallback={null}` at 198, 212, 227                             |
| R-10 (switcher) | Read `search` through `useEffectEvent`; the effect depends on `query`, `request` and open state; test that a new function identity does not re-run the search                                            | `switcher.tsx:180` deps `[search, query, request]`                                                    |
| R-11            | Per Q2: `announce()` the error once per failure and the retry result; no `role="alert"`                                                                                                                  | `switcher.tsx:149-164` announces only the count                                                       |
| R-39            | Mark messages as seen on close as well as on open, so ones that arrive while it is open don't show the dot again                                                                                         | `activity-indicator.tsx:27-33` sets `seen` on open only                                               |
| R-41            | A collapsed page crumb with no link is not a dead menu item (render it as text or drop `onAction`-less items); an org or project crumb in last position gets `aria-current="page"`                       | `breadcrumbs.tsx:131-145`, 84-95                                                                      |
| R-44            | Return focus through a ref on the help button (`IconButton` ref), not `querySelector("button")`                                                                                                          | `app-shell.tsx:205`                                                                                   |
| R-45            | Plain strings in the `widths` map                                                                                                                                                                        | `drawer.tsx:18-20`                                                                                    |

- **Files**: `console/src/ui/shell/breadcrumbs.tsx`, `switcher.tsx`, `app-shell.tsx`, `activity-indicator.tsx`, `drawer.tsx`, their tests in `src/ui/shell/`, and the gallery's `src/gallery/shell.test.tsx` and `rtl.test.tsx` (tests only; batch 2 does not touch these two files).
- **Verify**: a 320 px shell test (org button at least 24 by 24, the last crumb's visible text is the full name or truncates with a tooltip, no horizontal scroll); switcher tests for the current item, an error announcement, and a re-render with a new `search` function; drawer close returns focus to the opener; `pnpm size` (R-9's preload must not move the 230 KB first load); `pnpm test:engines` for the shell tests.

### Batch 2. Gallery and pseudo locale (L)

| ID             | Fix                                                                                                                                                                                                                 | Evidence on `main`                                                             |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| G-2            | Pseudo catalog grows each message by about 35% with placeholders intact (a Lingui option or a post-extract step), so UX-34's 30% growth shows                                                                       | `console/lingui.config.ts` sets `pseudoLocale` only                            |
| G-3            | A gallery `pseudoize()` helper with the same transform for scene strings (outside the production catalog, GAL-3); `lang` becomes a valid tag such as `en-XA` while pseudo is on                                     | scenes use plain literals; `lang="pseudo-LOCALE"`                              |
| G-4            | A Scenario switch (Slow, Fail, Empty) for the fixtures and a static States scene for the field family. Running jobs in `ActivityIndicator` waits for feature 46                                                     | `src/gallery/fixtures.ts` always resolves at once                              |
| G-6            | A state column in the button scene forcing `data-hovered`, `data-pressed`, `data-focus-visible`; a Preview group (reduced motion, more contrast, forced colors) or a note pointing at the browser's rendering panel | `frame.tsx` toolbar has theme, density, locale, direction, width only          |
| G-10           | A real sign in form in `AuthLayout` with `lg` fields and button and an `InlineError`; delete the milestone 3 sentence                                                                                               | `scenes/auth-scene.tsx:9`                                                      |
| G-11           | An editable field in the `SettingsCard` scene so pristine and dirty both show (who enforces FRM-5 is Q9)                                                                                                            | `scenes/data-scene.tsx`; `settings-card.tsx:9` leaves the footer to the caller |
| G-12           | Each scene sets its title (`Primitives · Gallery`) through `useDocumentTitle`                                                                                                                                       | `frame.tsx` and `router.tsx` keep "Orvano gallery"                             |
| G-13           | Scroll the scene container to the top on route change                                                                                                                                                               | `frame.tsx` scene `div.overflow-auto`                                          |
| G-14           | A "Reset gallery" button that clears `orvano:v1:*` and reloads                                                                                                                                                      | no reset in `frame.tsx`                                                        |
| G-15           | Two columns or a disclosure for the toolbar below 640 px, a toolbar skip link, `role="group"` with a label instead of `role="toolbar"`                                                                              | `frame.tsx` `role="toolbar"`                                                   |
| G-16           | A 320 px width option; change the frame's hash instead of remounting the `iframe`                                                                                                                                   | `frame.tsx:76` `iframe` keyed by URL                                           |
| G-21           | One realistic message per banner tone, each with an action in one variant                                                                                                                                           | `scenes/feedback-scene.tsx`                                                    |
| G-22 (a, b, c) | Unit in help text, not the label; an empty check before the length check in the scene's password schema; a placeholder for the optional combobox                                                                    | `scenes/forms-scene.tsx`                                                       |
| G-24           | Demo section headings one level above the components' own titles, or not headings                                                                                                                                   | scenes use `h2` throughout                                                     |
| G-28           | Reuse `SettingsCard`, `Table` and `DangerZone` content in the account and org scenes                                                                                                                                | `scenes/shell-scene.tsx` account placeholder                                   |

- **Files**: `console/lingui.config.ts`, a pseudo transform module or script if Lingui has no padding option, `console/src/locales/pseudo-LOCALE/messages.po` (generated), and everything under `console/src/gallery/` except `shell.test.tsx` and `rtl.test.tsx`.
- **Verify**: with Pseudo on, a shell string is at least 30% longer than English (a unit test on the transform, with ICU placeholders kept); `gallery.test.tsx` covers the new scenes and the States scene with axe; `node tooling/scripts/check-gallery-not-shipped.mjs` after `pnpm build` still passes, and nothing outside `src/gallery` imports the helper. The batch is large but dev only; split it into frame plus pseudo, then scenes, in one session if that is easier to review.

### Batch 3. Overlays, menus and palette (M)

| ID                     | Fix                                                                                                                                                      | Evidence on `main`                                                                    |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| R-7                    | A `description` on `Dialog` linked with `aria-describedby` (React Aria `Text slot="description"`), used by `ConfirmDelete` and `UnsavedChangesGuard`     | `dialog.tsx` has no description link                                                  |
| R-8                    | Filter `Menu`'s `onAction` for unavailable keys; keep the `MutationObserver` only if no React Aria hook exists, with a comment naming the version        | `menu.tsx:45`, 53-70, 122                                                             |
| R-10 (`ConfirmDelete`) | Read `loadDependents` through `useEffectEvent`; test a re-render with a new function                                                                     | `confirm-delete/index.tsx:188` deps `[load]`                                          |
| R-12                   | Keep `onClosed` in a ref and call it from the frame callback, not the Effect Event                                                                       | `docs-pane/index.tsx:83`, 107-108                                                     |
| R-22                   | Normalise locale digits to ASCII and compare the whole trimmed string; show the failure through `InlineError`/`useErrorCopy` so the kind picks the words | `confirm-delete/index.tsx:87` `replace(/\D/g, "")`, 89 fixed wording                  |
| R-25                   | `transition-[opacity,scale]` instead of `transition` in the three places; a theme switch test reads `transitionProperty`                                 | `popover.tsx:26`, `dialog.tsx:64`, `command-palette/index.tsx:53`                     |
| R-40                   | Humanise the unknown kind fallback (`foo bar`)                                                                                                           | `confirm-delete/dependents.ts:49`                                                     |
| G-20                   | With an empty query, leave recent actions out of the Go to group (or skip duplicates)                                                                    | `command-palette/index.tsx:93-97`: `matching` is every action when the query is empty |
| G-23                   | Product names as placeholders (`{product}`, `{host}`) so they are not translated or pseudo localized                                                     | `docs-pane/index.tsx:129-134`                                                         |

- **Files**: `console/src/ui/dialog.tsx`, `popover.tsx`, `menu.tsx`, `command-palette/index.tsx`, `confirm-delete/index.tsx`, `confirm-delete/dependents.ts`, `form/unsaved-changes-guard.tsx`, `docs-pane/index.tsx`, and their tests. It calls `useErrorCopy` but does not edit `error-copy.tsx` (batch 4).
- **Verify**: the alert dialogs have `aria-describedby` (axe plus a direct assertion); a `Menu` `onAction` test on an unavailable item; ConfirmDelete tests for "1x0x0x1", Arabic digits and a `forbidden` failure; the transition test; `pnpm test:engines`.

### Batch 4. Forms, feedback and shortcuts (M)

| ID          | Fix                                                                                                                                                                    | Evidence on `main`                                                       |
| ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| R-18        | Listen for `pointerup` and `pointercancel` in `afterPress` and drop the other when one fires; move the module listeners into a hook with cleanup                       | `form/field.tsx:24-48`: the deferred listener waits for `pointerup` only |
| R-20        | Add `unauthenticated`, `conflict`, `validation`, `server` copy; `retryable` only where a retry can help; per Q2, `ErrorState` that replaces content calls `announce()` | `error-copy.tsx:42-72` five kinds; `states.tsx:73`                       |
| R-21        | Match modifier shortcuts on `event.code` for letters, `event.key` for symbols; test `key: "л", code: "KeyK"`                                                           | `use-shortcut.ts:53`                                                     |
| R-23        | Compare watched params with a stable serialisation                                                                                                                     | `form/unsaved-changes.ts:78` `!==`                                       |
| R-38        | An identical toast restarts its timer and is announced again                                                                                                           | `toast/index.tsx:46-55` only bumps the count                             |
| R-43        | Honour `isDisabled` and `isReadOnly` in the custom `Field`, or drop them from its props                                                                                | `form/field.tsx:20-21`                                                   |
| R-46        | Skip `event.repeat`                                                                                                                                                    | `use-shortcut.ts` has no repeat check                                    |
| G-7         | `dir="ltr"` and `unicode-bidi: isolate` on the outer `Kbd`                                                                                                             | `kbd.tsx:59`                                                             |
| G-22 (d, e) | The password show and hide toggle inside the field's end; `aria-invalid` on the `Select` trigger when invalid                                                          | `form/text-fields.tsx`, `form/choice-fields.tsx`                         |

- **Files**: `console/src/ui/form/field.tsx`, `form/unsaved-changes.ts`, `form/text-fields.tsx`, `form/choice-fields.tsx`, `error-copy.tsx`, `states.tsx`, `use-shortcut.ts`, `toast/index.tsx`, `kbd.tsx`, and their tests.
- **Verify**: a `pointercancel` test for the field error; one test per error kind (copy and Retry shown or not); the non Latin layout and `event.repeat` shortcut tests; a repeated toast test (timer restarted, announced twice); a `Kbd` DOM order test under `dir="rtl"`; a failed submit marks the `Select` trigger `aria-invalid`.

### Batch 5. Data display, lists and storage (M)

| ID   | Fix                                                                                                                                                 | Evidence on `main`                                        |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| R-13 | Keep the 4 px line and widen the hit area with a transparent `before:` element to 24 px                                                             | `shell/resource-layout.tsx:230` `w-1`                     |
| R-14 | Announce the filtered count after it settles; `toLocaleLowerCase(i18n.locale)`; `ContentSkeleton` through `useDelayedPending` instead of "Loading…" | `shell/resource-list.tsx:44-45`, 72                       |
| R-15 | `scroll-pt-(--row-height)` on the `Table` scroll wrapper. The `html` rule in `theme.css` stays (it helps `AuthLayout` and the error page)           | `table/index.tsx:50`, 83                                  |
| R-16 | Reset `SecretReveal` when `secret` changes (an effect keyed on it, or document that callers key it by the key's id) and test a rerender             | `copy-field.tsx:104` `useState(created)`                  |
| R-17 | `role="region"` and `aria-label` on the `CodeSnippet` `pre`, `aria-describedby` to the hint, and `announce(hint)` in the manual branch of `useCopy` | `copy-field.tsx:160` `tabIndex={0}` with no name          |
| R-19 | Render the raw value or an em dash for an invalid date; a `focusable` prop, off in tables and lists                                                 | `relative-time.tsx:65` `toISOString()`, 67 `tabIndex={0}` |
| R-31 | The "before the delay" test uses a 100 ms load                                                                                                      | `skeleton.test.tsx:40-41` 250 ms                          |
| R-33 | `ExternalLink` renders only `http:` and `https:` hrefs as links                                                                                     | `link.tsx:94-109`                                         |
| R-34 | Check `event.storageArea === localStorage` and treat a null key as a reset                                                                          | `shared/storage/stored.ts:96`                             |
| R-35 | Unsubscribe the old user's layout store on user change                                                                                              | `shared/stores/layout.ts:11-16`                           |
| R-36 | `Intl.Segmenter` for the first grapheme                                                                                                             | `avatar.tsx:10`                                           |
| R-37 | Floor inside the unit so the step to the next unit is at the boundary                                                                               | `shared/format/index.ts:55-57`                            |
| R-42 | `aria-busy` on the region being loaded (a prop or the caller's container), keep the hidden text                                                     | `skeleton.tsx:83`                                         |
| G-17 | A 24 px minimum height for a standalone `ExternalLink` (padding or a size variant)                                                                  | `link.tsx:101` inline variant only                        |
| G-18 | Scrollbar padding at the bottom of the table's scroll container, an accessible name for it, `whitespace-nowrap` for masked keys                     | `table/index.tsx:50`                                      |

- **Files**: `console/src/ui/shell/resource-layout.tsx`, `shell/resource-list.tsx`, `table/index.tsx`, `copy-field.tsx`, `relative-time.tsx`, `link.tsx`, `avatar.tsx`, `skeleton.tsx`, `skeleton.test.tsx`, `console/src/shared/format/index.ts`, `shared/storage/stored.ts`, `shared/stores/layout.ts`, and their tests. Not `theme.css` (batch 6).
- **Verify**: the separator's hit box is 24 px wide (`getBoundingClientRect` of the pseudo element's host); the `Table` ArrowDown test from R-15 in a short viewport; an invalid date renders; the `SecretReveal` rerender; storage event tests for `sessionStorage` and `clear()`; Turkish `İ` filter test; `pnpm test:engines`.

### Batch 6. Theme, lint and CI tooling (M)

| ID   | Fix                                                                                                                                                                                                                               | Evidence on `main`                                                                |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| R-24 | Give the `prefers-contrast: more` block the same selector as the dark fallback; a test with the boot script off, dark plus more contrast                                                                                          | `theme.css:198` (`:root:not([data-theme])`), 232                                  |
| R-27 | Scope the forced colors rule to what AC-6 names and use `outline` or an inset `box-shadow` so nothing moves                                                                                                                       | `theme.css:241-247` `[data-selected]`                                             |
| R-28 | Add `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write` to SEC-2; widen STY-5's arbitrary pattern to variants; check template quasis in `cx(...)`. One fixture each                                                  | `tooling/config/console-restrictions.ts` names none of the four sinks             |
| R-29 | A tooling test that the CSP in `vite.config.ts` equals `SecurityHeaders.cs` (or one shared JSON). The scripted interaction in `csp.spec.ts` needs a page that hosts overlays: do it now if `/` can open one, else with feature 13 | `console/vite.config.ts:11` copies the policy; `e2e/csp.spec.ts` only loads pages |
| R-30 | Also scan `dist/assets/*.js` for a string only the gallery fixtures contain (for example `usr_ada`), without editing `src/gallery`                                                                                                | `check-gallery-not-shipped.mjs:20-25` manifest only                               |
| R-32 | Print the ten largest modules of the shell chunks as a CI artifact in the size step. The representative nav run waits for feature 13                                                                                              | `console/scripts/size-entries.mjs`, `_console.yml` have no breakdown              |

- **Files**: `console/src/ui/theme.css` (rules only, no token values) and its tests, `tooling/config/console-restrictions.ts`, `tooling/lint-fixtures/`, `tooling/scripts/console-restrictions.test.mjs`, `check-lint-fixtures.test.mjs`, `check-gallery-not-shipped.mjs` and its test, a new CSP equality test under `tooling/scripts/`, `console/vite.config.ts`, `console/e2e/csp.spec.ts`, `console/scripts/size-entries.mjs`, `.github/workflows/_console.yml`. It reads `server/src/Orvano.Host/Web/SecurityHeaders.cs` and does not edit it.
- **Verify**: `pnpm check:lint-fixtures` with the new fixtures firing and the rule ID list and count updated; `theme.contrast.test.ts` and a forced colors layout test (a checkbox keeps its size); the CSP equality test fails when one side changes; `pnpm build` then the gallery check; the CI workflow keeps `permissions: {}` and SHA pinned actions (spec 0008 CI-4).

### Batch 7, after the decisions (S)

Not parallel with 1 to 6: it lands once Q4, Q6 and Q7 are answered and batches 1, 2 and 5 are merged, since it touches their files.

- Q4 (G-5): the gallery's right to left locale in `src/gallery/frame.tsx`, `providers.tsx`, `render-scene.tsx`, and `rtl.test.tsx`.
- Q6 (G-8): user supplied names in an isolate (`<bdi>` or `dir="auto"`) in `ResourceList`, the crumbs, the switchers and table cells, with a long Latin name in the right to left fixtures.
- Q7 (G-27): `RouteTabs` overflow and four to six tabs in the database scene.
- The combined check from the gallery review: G-1's 320 px top bar with G-2's padded pseudo locale on.

## Questions for `/architect`

Each is a separate decision. The recommended answer is what the batches assume.

1. **Q1. What does the top bar do below 400 px? (G-1; design.md _Shell_, AC-8)** Today design.md collapses only the middle crumbs below 64rem, which leaves about 70 px for the org crumb and the page at 320 px. **Recommended**: below 25rem the breadcrumbs show only the ellipsis menu (which already holds org and project items that open their switchers) and the current page crumb; every crumb control is at least 24 by 24 px; the top bar's actions keep their order (principle 5, so the activity button does not move into the account menu). AC-8 keeps its words; design.md _Shell_ gets the rule.
2. **Q2. Which live regions does AC-12 allow? (K-2, K-3, R-11, R-20)** AC-12 names the `Announcer`, React Aria's toast region and `Banner`, but `InlineError` already uses `role="alert"`, which A11Y-4 allows. R-11 and R-20 each ask for one more. **Recommended**: add `InlineError` to AC-12's list, and add no other region: the switcher error (R-11) and an `ErrorState` that replaces content (R-20) speak through `announce()`, the existing `Announcer`. Batches 1 and 4 build that way.
3. **Q3. Does the palette follow listbox or menu? (K-1)** AC-13 says "combobox and listbox patterns"; PRM-8 and the code use React Aria's `Autocomplete` with a `Menu`. **Recommended**: amend AC-13 to "the APG dialog plus combobox pattern, with a menu of actions (`Autocomplete` with `Menu`, PRM-8)". No code change; SRT-10 already reads it as a combobox.
4. **Q4. `ar` or `ar-XB` for right to left? (G-5; RTL-1, AC-18, design.md _Right to left_)** RTL-1 and AC-18 name `ar-XB`, and so do the gallery (`frame.tsx:60`, `providers.tsx:24`, `render-scene.tsx:25`) and `src/gallery/rtl.test.tsx:14`; `console/AGENTS.md` says CI's WebKit resolves `ar-XB` as left to right and tests use `ar`. **Recommended**: name the real `ar` locale in RTL-1 and AC-18 for both the gallery and the test (Lingui keeps the active catalog, so no catalog is needed), say why, and keep `ar-XB` out. Batch 7 changes the four files.
5. **Q5. Are dialog and sheet titles `--text-md` or `--text-lg`? (R-26)** The tokens and design.md _Typography_ say `md`; `Dialog`, `SideSheet` and the docs pane use `text-lg` (`dialog.tsx:69`, `side-sheet/index.tsx:63`, `docs-pane/index.tsx:120`). **Recommended**: keep `text-lg` (20 px reads well as a dialog title and matches what was verified), and move "sheet and dialog titles" to the `--text-lg` uses in the `theme.css` comments that generate design.md's table. That edit is to `theme.css`, so it goes in batch 6 or after it.
6. **Q6. How is user supplied text set in right to left? (G-8; design.md _Right to left_)** **Recommended**: names, IDs and keys render in an isolate (`<bdi>` or `dir="auto"`), so truncation keeps their start in their own direction, with the full text as accessible name and tooltip (UX-34). Batch 7 builds it.
7. **Q7. What does `RouteTabs` do on overflow? (G-27; design.md _Components_)** **Recommended**: the tab row scrolls inline with the current tab scrolled into view and a fade at the cut edge; no More menu, so the tabs stay plain links (UX-5). Batch 7 builds it and adds tabs to the scene.
8. **Q8. Does `Switch` get `unavailableReason`? (G-29; PRM-4)** PRM-4 lists `Button`, `IconButton` and menu items only. **Recommended**: no new prop: a switch the caller can't change is `isDisabled` with its reason as the field's description text, and design.md _Forms_ says so. Batch 2 renames the gallery case and shows the description.
9. **Q9. Who enforces "Update enabled only when changed"? (G-11; FRM-5, design.md _Data display_)** `SettingsCard` takes a `footer` and does not know the form state (`settings-card.tsx:9-14`). **Recommended**: the caller, through the form's dirty state; design.md says so. Batch 2 shows pristine and dirty in the scene.
10. **Q10. Amend GAL-2, AC-20 and I18N-7 for the gallery? (G-2, G-3, G-4, G-12, G-14, G-16)** **Recommended**: yes, add a 320 px width frame (AC-8 names it), a reset control, the scenario switch and the field States scene, a pseudo locale that grows text by at least 30% (UX-34) and covers scene strings without entering the catalog, and a title per scene. Batch 2 builds to it; the spec edit can follow.
11. **Q11. Which steps does `verify.md` add before the screen reader run? (R-7, R-11, R-17, G-9, G-18, G-19, G-25)** **Recommended**: SRT-6 hears the alert dialog's description; SRT-4 hears the current project and a search error; SRT-9 reaches the named code snippet; SRT-8 hears one toast once for each of success, info and error; a table step hears the scroll region's name; SRT-1 notes that the skip link also skips the resource list. Add them before AC-22 is run.
12. **Q12. Reconcile the stale lines in specs 0001 and 0003. (K-4)** `/sync` flagged spec 0003 lines 27, 141 (RTE-6), 647 and 659 ("verify with feature 11", now proven by `link.test.tsx`), 667 (feature 11's build list unticked though built) and 307, 651 (styling and grid shown as open), and spec 0001 lines 310 (the grid choice, now `react-data-grid`) and 322 (open until AC-22, which spec 0015's Follow-up covers). **Recommended**: one `/architect` pass that records each as settled by spec 0015 and points line 322 at AC-22.
13. **Q13. One docs pass for the design.md lines the fixes need?** Lines with no open decision: `Kbd` keys always read left to right (G-7); the switcher marks the current item with `aria-current` (G-9); every gallery scene sets its title, in `src/ui/AGENTS.md` or spec 0015 (G-12); product names are message placeholders (G-23, I18N-4); a drawer has a visible close button (G-26); the separator's hit area is 24 px (R-13). **Recommended**: yes, one `/architect` docs edit with Q1 and Q5 to Q9, so no fix batch edits `design.md` and the batches stay apart.

## Deferred

| ID   | To                                        | Why                                                                                                                                                                  |
| ---- | ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| G-19 | AC-22, the screen reader run (feature 11) | Double or assertive toast announcement is a guess until someone listens; Q11 adds the step. If it shows, a polite announcer for success and info is a small fix then |
| G-25 | Feature 16, tables and rows API           | A roving tab stop only matters once a resource list can hold hundreds of tables; the lists are links (navigation) and the skip link already passes them              |

Parts of ready items that wait: R-29's scripted overlay interaction (feature 13, console accounts and projects, if no page today hosts overlays), R-32's representative nav probe (feature 13, when the `_shell` routes land), and G-4's running jobs in `ActivityIndicator` (feature 46, request and function logs, which AC-12 names for the jobs list).

## Won't fix

| ID   | Reason                                                                                                                                                                                                          |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| G-30 | The review itself asks for no change: UX-24 keeps errors until dismissed, success toasts close after 6 s, and the region never covers the focused element (AC-12). Batch 2 may add a line to the feedback scene |

Parts declined inside ready items: R-9's hidden input that swallows keystrokes (it takes focus from the user's own field, which is worse than the gap; preload and the busy state fix the cause); R-8's request to React Aria (outside this repo); G-1's move of the activity button into the account menu (principle 5, same frame everywhere); G-15's moving the toolbar after the scene in DOM order (a toolbar skip link does the same with less surprise).
