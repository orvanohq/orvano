# Review, design system and console shell (spec 0015), 2026-10-27

**Reviewed by**: Claude Sonnet 5.5 (the code was written by a different model), one reviewer reading by area
**Scope**: `orvanohq/orvano` `main` at `e090b07`, the hand written code of PRs #108, #110, #111, #112, #113, #115, #117 and #119: `console/src/ui/`, `console/src/shared/stores/` and `storage/`, `console/src/main.tsx` and the root route, the theme (`theme.css`, `public/theme-boot.js`), `tooling/config/console-restrictions.ts`, `console/scripts/size-entries.mjs`, `tooling/scripts/design-tokens.mjs` and `check-gallery-not-shipped.mjs`, `console/vite.config.ts`, `console/e2e/`, and `.github/workflows/_console.yml`. The gallery scenes, the `.po` catalogs and the generated files were skimmed, not reviewed line by line. The test files were read where a finding is about test quality.
**Verdict**: Changes requested (0 blockers, 5 majors, 28 minors, 13 nits)

## Summary

This is careful, well documented work. The primitives follow one pattern (React Aria inside, tokens and `data-*` states outside, no `className` in). The unavailable button wrapper, the stores, the form timing and the unsaved changes guard each live in one place. Colors are tokens, contrast is tested, the CSP test and the gallery check exist, and almost every public prop has a comment that names the rule it serves. Nothing here blocks a merge that already happened, and nothing leaks data or opens a script injection path.

The five majors are seams that feature 13 (the real shell routes) and the first translated catalog will hit:

1. The real app never gives React Aria the active locale, so its own strings, number and date formats, and right to left key mirroring follow the browser instead of the console (I18N-6, RTL-1). Only the gallery wires it.
2. `@react-aria/optimize-locales-plugin` (I18N-6) is not in the build, so every React Aria language ships. The shell has 11 KB of headroom under its 230 KB budget.
3. `Dialog` centers with `items-center` inside `overflow-y-auto`, which cuts off the top of a dialog taller than the window (small phones, short windows, 400% zoom, AC-8).
4. `ConfirmDelete` can be closed with Escape while the delete request is in flight. If the request then fails, the error is set on a dialog that is gone, and nobody is told (UX-22, UX-23).
5. `ResourceLayout` reads its saved width and collapsed state once, so the same component reused for another service shows, and then writes, the first service's layout (SHL-13).

I read the code and the existing tests; I did not run the gallery, the test suite or the screen reader script, because the worktree has no `node_modules` and disk is tight. Finding 3 depends on browser layout, so it says how to confirm it in the gallery in one minute.

### Suggested fix groups

Grouped so the fix sessions do not touch the same files. Numbers are the finding numbers below.

| Group                     | Findings                  | Files                                                                                                                                |
| ------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| A. Locale and bundle      | 1, 2, 6                   | `main.tsx`, `routes/__root.tsx`, `shared/i18n`, `vite.config.ts`, `shell/switcher.tsx` (label only)                                  |
| B. Overlays and focus     | 3, 4, 7, 8, 9, 10, 11, 12 | `dialog.tsx`, `confirm-delete/`, `menu.tsx`, `popover.tsx`, `side-sheet/`, `docs-pane/`, `shell/app-shell.tsx`, `shell/switcher.tsx` |
| C. Shell layout and state | 5, 13, 14, 15             | `shell/resource-layout.tsx`, `shell/resource-list.tsx`, `shell/breadcrumbs.tsx`, `table/`, `theme.css`                               |
| D. Primitives and forms   | 16 to 23                  | `copy-field.tsx`, `form/`, `relative-time.tsx`, `error-copy.tsx`, `states.tsx`, `use-shortcut.ts`                                    |
| E. Theme and styles       | 24 to 27                  | `theme.css`, `dialog.tsx` and `popover.tsx` (classes only)                                                                           |
| F. Tooling, CI and tests  | 28 to 33                  | `console-restrictions.ts`, `vite.config.ts`, `e2e/`, `check-gallery-not-shipped.mjs`, `skeleton.test.tsx`                            |

Groups B and E both touch `dialog.tsx` and `popover.tsx`; run E after B, or do both in one session.

## Major

### 🟠 1. The real app never gives React Aria its locale, `console/src/main.tsx:36-43`, `console/src/routes/__root.tsx:19-26`, `console/src/shared/i18n/index.ts:33-35`

**Problem**: I18N-6 says changing the locale "sets `<html lang dir>` and React Aria's `I18nProvider` locale". `activateLocale` sets `lang` and `dir` but never React Aria's locale. `LocaleProvider` (`ui/locale-provider.tsx`) is mounted only by the gallery (`gallery/providers.tsx`, `render-scene.tsx`) and the tests; `main.tsx` and the root route do not use it. In the real app React Aria reads `navigator.language` on its own.
**Why it matters**: React Aria's built in strings (the hidden "Dismiss" button in popovers, "Clear search", the table and number field labels, live announcements), its number and date formats, and the direction it uses to mirror arrow keys all follow the browser, not the console. When the account locale (feature 13) or a stored choice differs from the browser language, an Arabic console on an English browser has `dir="rtl"` on `<html>` but React Aria still moves focus as left to right (menus, tabs, the palette). RTL-1 and AC-18 prove right to left only in the gallery, so no test would catch this. Rules: I18N-6, RTL-1, AC-18.
**Suggested fix**: wrap the app in `LocaleProvider locale={i18n.locale}` inside Lingui's provider (in `main.tsx`, or the root route), and re render it when the locale changes. Add a browser test that renders the real route tree with `dir="rtl"` and a non English active locale and checks that an arrow key moves the other way in a `Menu`.

### 🟠 2. `@react-aria/optimize-locales-plugin` is missing, so every React Aria language ships, `console/vite.config.ts:16-27`

**Problem**: spec 0003 I18N-6 requires the plugin (listed before `react()`, with `locales` derived from the shipped catalogs, `en` mapped to `en-US`) and records that it was confirmed under Vite 8 with Rolldown, saving about 16 KB gzip on a DatePicker bundle. `grep` finds it in no `package.json` or Vite config. Spec 0015 does not drop the requirement.
**Why it matters**: feature 11 is the first feature to use React Aria widely (Table, Autocomplete, Menu, Select, ComboBox, Toast). The latest reverify report puts the shell first load at 218.72 KB of 230, so 11 KB of headroom is all feature 13 and the translated catalogs get. Every language's strings for every imported widget are in that number. I did not measure the saving for this set of widgets. Rules: I18N-6, STY-8, AC-3.
**Suggested fix**: add the plugin as the spec shows it, with `locales` read from `shippedLocales` in `shared/i18n/locales.ts` (plus the region tag mapping), and re run `pnpm size` to record the new numbers in the PR. Check that the RTL test still passes: React Aria falls back to `en-US` strings for `ar-XB`, which is what RTL-1 relies on.

### 🟠 3. A dialog taller than the window loses its top, `console/src/ui/dialog.tsx:54`

**Problem**: the overlay is `fixed inset-0 flex items-center justify-center overflow-y-auto p-4`. In a flex container, `align-items: center` with overflow puts equal parts of the overflow above and below the box, and the part above the scroll origin can never be scrolled to. The `Modal` child has no `max-h`, no `my-auto` and no inner scroll area.
**Why it matters**: `ConfirmDelete` with a dependents list, `ShortcutHelp` (a long `dl`, `size="md"`), and the discard question are the tall ones. At 320 px wide, 400% zoom, or a landscape phone, the title, and the description under it, are cut off with no way to reach them. That is a reflow failure (WCAG 1.4.10) and a loss of the dialog's name for sighted keyboard users. Rules: AC-8, A11Y reflow, UX-6. I read this from the CSS and did not run it. To confirm, open the gallery overlays scene at 320x480 and open `ShortcutHelp`.
**Suggested fix**: make the overlay a plain scroll container and center with margins: `fixed inset-0 overflow-y-auto p-4` and on `Modal` `mx-auto my-auto` inside a `flex min-h-full` wrapper, or `max-h-[calc(100dvh-2rem)]` on `Modal` with the body scrolling (the way `SideSheet` already does). Add a test at a 320 by 400 viewport that the heading's top edge is at or below 0.

### 🟠 4. Escape closes `ConfirmDelete` while the delete runs, and a failure then goes nowhere, `console/src/ui/confirm-delete/index.tsx:71-101, 104-125`

**Problem**: `remove()` sets `isDeleting`, awaits `onConfirm()`, and on a rejection sets `failed` and announces. Nothing blocks `onOpenChange(false)` while `isDeleting` is true: Escape, the Cancel button (`slot="close"`), and React Aria's own handling all call `openChange(false)`, which resets `failed` and closes the dialog. The request keeps running. If it then rejects, `setFailed(true)` runs on a closed dialog; the next time the dialog opens it shows an old state, and in between the user saw no error. If it resolves, `openChange(false)` runs a second time.
**Why it matters**: the dialog is the last gate before destroying data. A user who presses Escape because it is slow believes they cancelled. If the delete in fact succeeds they lose something they cancelled; if it fails they think it worked. UX-22 says a rejection keeps the dialog open with an inline error; UX-23 gives this dialog the safest behaviour in the console. Rules: UX-22, UX-23, AC-17 (`ConfirmDelete`).
**Suggested fix**: while `isDeleting`, make `openChange` ignore close requests (and set `isDismissable` and Escape off through the `Dialog` props, so the key is not swallowed silently), and disable Cancel with `isDisabled` (this is the "truly can't act" case in PRM-4). Add tests: Escape during a pending delete does nothing; a rejection after a pending Escape shows the inline error.

### 🟠 5. `ResourceLayout` keeps the first service's saved layout, `console/src/ui/shell/resource-layout.tsx:47-57`

**Problem**: `ids` is rebuilt on every render from `service`, but `saved` comes from `useState(() => resourceListLayout.read(ids))`, which runs once. If the same `ResourceLayout` element stays mounted while `service` changes (a layout route that renders `<ResourceLayout service={...}>` for databases, then storage, which React reconciles as the same component at the same position), the width and collapsed flag stay those of the first service. The next `save()` then writes `{...saved, ...next}` under the new service's key, copying the old width into it.
**Why it matters**: SHL-13 and UX-2 store width per service (`orvano:v1:{userId}:{projectId}:svc:{service}`). The failure is silent (a wrong width, and a changed key in storage) and appears only when feature 13 wires several services through one shell route. The same applies to a changed `projectId` (project switch). Rules: SHL-13, UX-2, STO-2.
**Suggested fix**: key the layout state by its storage key: either render the stateful part in an inner component with `key={storageKey}`, or re read in the render (`useSyncExternalStore` over `resourceListLayout.subscribe`, which also gives STO-5 for free). Add a test that rerenders with a new `service` and a stored width for each.

## Minor

**Locale and bundle**

### 🟡 6. Switcher trigger name is built with string concatenation, `console/src/ui/shell/switcher.tsx:89`

**Problem**: `aria-label={`${strings.dialog}: ${props.name}`}` joins a translated phrase, a hard coded `": "`, and the name. Word order and punctuation are the translator's job (I18N-1, I18N-8); some languages put the name first.
**Suggested fix**: one message per kind: ``t`Switch project: ${name}` `` and the same for organizations. The lint rule missed this because the template literal sits in `aria-label` (it is checked, but the expression form passes).

**Overlays, menus and focus**

### 🟡 7. Alert dialogs have no description link, `console/src/ui/dialog.tsx:62`, `console/src/ui/confirm-delete/index.tsx:127`, `console/src/ui/form/unsaved-changes-guard.tsx:63`

**Problem**: the APG alert dialog pattern asks for `aria-describedby` pointing at the message ("This deletes X. You can't undo this."). React Aria's `Dialog` labels by the `Heading slot="title"` but does not link a description. Screen readers read only the title when focus lands on Cancel.
**Suggested fix**: give `Dialog` a `description` prop (or wrap the children's first paragraph in `Text slot="description"` and set `aria-describedby`), and use it in `ConfirmDelete` and the guard. Add it to SRT-6.

### 🟡 8. A menu level `onAction` bypasses `unavailableReason`, and the workaround leans on a mutation observer, `console/src/ui/menu.tsx:45, 56-70, 105-123`

**Problem**: `MenuItem` drops its own `onAction` when unavailable, but `Menu`'s `onAction(key)` (the palette uses it) still fires for that item, so the "ignores Enter and presses" promise in PRM-4 holds only for item level handlers. Separately, `aria-disabled` is added by a `MutationObserver` on the whole menu subtree after React renders it: there is a frame where the item is announced as enabled, the observer re runs `querySelectorAll` on every mutation (each keystroke in the palette re renders its items), and a React Aria release that starts to set the attribute itself will fight it.
**Suggested fix**: filter in `Menu` (wrap `onAction` and skip keys whose element has `data-unavailable`, or track unavailable keys in context), and ask React Aria for `aria-disabled` support or set `isDisabled` with `aria-disabled` focus handling through `disabledKeys` plus a focusable override. Keep the observer only if no better hook exists, and add a comment that says which React Aria version made it necessary.

### 🟡 9. First open of a lazy panel gives no feedback, and typed keys go to the page, `console/src/ui/shell/app-shell.tsx:26-34, 76-87, 211-230`

**Problem**: the palette, shortcut list and docs pane are `lazy`, mounted on first use inside `<Suspense fallback={null}>`. Pressing Mod+K the first time sets `used.palette`, then the chunk loads with nothing on screen. On a slow or flaky link, the user keeps typing "datab", and those characters land in whatever had focus (a form field, for example, since Mod+K works from fields) before the palette opens and steals focus. Nothing tells them anything is loading.
**Why it matters**: PRM-10 asks for lazy loading, not for a silent gap. Keystrokes into a field behind the palette are data entry the user did not intend. Rules: PRM-10, AC-13.
**Suggested fix**: preload on idle (`requestIdleCallback(() => import(...))` after the shell mounts) and on `pointerenter` and `focus` of the palette and help buttons; show a small busy state if the chunk is not ready after 300 ms (the same `useDelayedPending` rule); and swallow keystrokes during that gap (focus a hidden input). The first load budget is not affected by idle preloads.

### 🟡 10. Switcher and `ConfirmDelete` re run their request when the callback changes identity, `console/src/ui/shell/switcher.tsx:155-178`, `console/src/ui/confirm-delete/index.tsx:169-183`

**Problem**: the effects list `search` and `load` as dependencies. `ShellModel.searchOrgs` and `searchProjects` will be closures built by the `_shell` route; if feature 13 builds them in render (the natural way), every re render of `AppShell` while the popover is open (a toast, a layout change, a route update) aborts and re sends the search, and re announces "3 projects". `loadDependents` has the same shape: a parent that re renders re fetches dependents and flashes "Counting what will be deleted…". The types and comments do not say the callbacks must be stable.
**Suggested fix**: read the callbacks through `useEffectEvent` (the shell already does this in `useShortcut`) and make the effect depend only on `query`, `request` and the open state; or document "must be stable" and test it. The same pattern, and a test that re rendering with a new function does not call it again.

### 🟡 11. Switcher errors are not announced, and "loading" never is, `console/src/ui/shell/switcher.tsx:238-251`

**Problem**: the result count is announced on success (`announceCount`), but the error paragraph appears with no `role="alert"` or `announce()`, and the "Loading projects…" empty text is not live. A screen reader user who types a query and gets a network failure hears nothing. SRT-4 only covers the happy path.
**Suggested fix**: add `role="alert"` to the error block (this makes it a fifth live region, so update AC-12's list, see the architect note), or call `announce(strings.error)` once per error, and announce the retry result.

### 🟡 12. The docs pane calls an Effect Event after its effect was cleaned up, `console/src/ui/docs-pane/index.tsx:100-110`

**Problem**: `gone()` comes from `useEffectEvent` and is called inside `requestAnimationFrame` from an effect cleanup. React documents Effect Events as callable only from inside effects; calling one after unmount works today because the callback reads a ref, but it is outside the contract and a future React can warn or throw. The same file has a second, older comment saying "once the pane (or its drawer) is gone".
**Suggested fix**: keep `onClosed` in a ref that the layout effect updates (the pattern three lines above) and call that from the frame callback.

**Shell layout and state**

### 🟡 13. The resize separator is a 4 px wide target, `console/src/ui/shell/resource-layout.tsx:216`

**Problem**: `w-1` is 4 px. WCAG 2.5.8 asks for 24 px targets unless the control has a spacing or an equivalent; UX-32 says "at least 24 px". The keyboard path is good, but a pointer or touch user has a very small handle to hit, and 2.5.7 asks for a single pointer alternative to dragging.
**Suggested fix**: keep the 4 px line and widen the hit area with a transparent `before:` element (`before:absolute before:inset-y-0 before:-inset-x-2`), and say so in `design.md`.

### 🟡 14. `ResourceList` filter has no result feedback and uses a locale blind lower case, `console/src/ui/shell/resource-list.tsx:44-45, 71-76`

**Problem**: typing in the filter changes the list but announces nothing (compare the switcher), "Loading…" is a bare `<p>`, and `toLocaleLowerCase()` without a locale makes `İ` and `I` compare wrongly in Turkish. UX-21 also asks for a skeleton after 300 ms, not text.
**Suggested fix**: `announce()` the count after the filter settles (reuse the switcher's plural), pass `i18n.locale`, and use `ContentSkeleton` through `useDelayedPending`.

### 🟡 15. A sticky table header can cover the focused cell, `console/src/ui/table/index.tsx:50, 82-85`, `console/src/ui/theme.css:282-285`

**Problem**: SHL-8 asks for `scroll-padding-top` equal to any sticky header. `theme.css` puts `scroll-padding-top: var(--spacing-topbar)` on `html`, but in the shell the scroller is `main` (and the top bar is not inside it), so the rule does nothing there. `Table` wraps itself in `overflow-auto` with a `sticky top-0` header and no `scroll-pt`, so arrow key navigation to a row can leave the focused cell under the header (WCAG 2.4.11).
**Suggested fix**: add `scroll-pt-(--row-height)` to the `Table` wrapper, and delete or justify the `html` rule (it still helps `AuthLayout` and the error page). Test: focus the last visible row and press ArrowDown in a short viewport.

**Primitives and forms**

### 🟡 16. `SecretReveal` keeps showing an old secret if its prop changes, `console/src/ui/copy-field.tsx:102-107`

**Problem**: the secret is copied into state once (`useState(created)`). Rotating a key with the component still mounted (a second "Create key" in the same sheet) passes a new `secret` that is ignored; the first, now stale, value stays on screen or, after "I've copied it", the stale masked value.
**Why it matters**: showing a secret that is not the one just created is the one mistake this component must not make (PRM-7).
**Suggested fix**: derive from props with a `key` (document that callers key it by the key's id) or reset state in an effect keyed on `created`; add a test that rerenders with a new secret.

### 🟡 17. `CodeSnippet` is a focusable block with no name, and its copy fallback is silent, `console/src/ui/copy-field.tsx:157-160, 184`

**Problem**: the `pre` takes `tabIndex={0}` but has no role or label, so a screen reader reaches "blank" or reads the code with no context. When the clipboard is refused the hint `<p>` appears with no live announcement and no `aria-describedby` from the `pre` (`CopyField` does link its hint). SRT-9 covers only the success path.
**Suggested fix**: `role="region"` with `aria-label={label}` on the `pre`, `aria-describedby` to the hint, and `announce(hint)` in the manual branch (put that once in `useCopy`).

### 🟡 18. A touch scroll can leave a field error unrevealed, `console/src/ui/form/field.tsx:24-48`

**Problem**: `afterPress` waits for `pointerup` when a blur happens during a press. A touch that turns into a scroll ends with `pointercancel`, never `pointerup`, so the deferred reveal listener (`once`, on `pointerup` only) stays registered, the error does not show, and it fires on some later, unrelated press. The module also adds three document listeners at import time with no cleanup, and `isPointerDown` is module global state shared by all forms.
**Suggested fix**: listen for both `pointerup` and `pointercancel` (and drop the other when one fires), or reveal immediately and keep the submit button stable by reserving space for the error row. Move the listeners into a hook with cleanup.

### 🟡 19. `RelativeTime` throws on an invalid date, and every instance is a tab stop, `console/src/ui/relative-time.tsx:52-70`

**Problem**: `new Date("garbage").toISOString()` throws `RangeError`, and `Intl.RelativeTimeFormat.format(NaN)` throws too. One bad `createdAt` from the API takes the whole subtree to the error boundary. Each `RelativeTime` also takes `tabIndex={0}` (UX-34, so the tooltip shows on focus); the activity list shows 50 and a table column of times puts one in every row, so tabbing crosses dozens of stops that add no action.
**Suggested fix**: render the raw string (or an em dash) when `Number.isNaN(date.getTime())`; show the exact time through `title` text for the table case, or give the tooltip to the whole row's cell, and make the focusable time opt in (`focusable` prop, true by default only outside tables and lists).

### 🟡 20. `ErrorState`, `InlineError` and `useErrorCopy` only know five kinds, `console/src/ui/error-copy.tsx:41-86`, `console/src/ui/states.tsx:73`

**Problem**: spec 0003 FLT-1 lists nine kinds (`network`, `timeout`, `unauthenticated`, `forbidden`, `not_found`, `conflict`, `validation`, `rate_limited`, `server`). `useErrorCopy` handles `not_found`, `forbidden`, `rate_limited`, `network`, `timeout`; `unauthenticated` ("Something went wrong" instead of "Sign in again"), `conflict` (a delete blocked by dependents reads "Try again in a moment") and `validation` fall to the generic text, and `server` shares it. `ErrorState` offers Retry for `conflict` and `unauthenticated`, where retrying cannot work. `ErrorState` is also not announced, since it has no live region, so a page that fails after load changes silently.
**Suggested fix**: add the missing kinds to the switch (one line each in `design.md` _Writing in the UI_), set `retryable` to the kinds where a retry can change the result, and use `role="alert"` on `ErrorState` when it replaces content that was already on screen (and add it to AC-12's list).

### 🟡 21. Shortcuts match on `event.key`, which fails on non Latin layouts, `console/src/ui/use-shortcut.ts:53`

**Problem**: Mod+K compares `event.key.toLowerCase()` to `"k"`. On a Russian, Greek, Hebrew or Arabic layout Ctrl+K reports `"л"`, `"κ"`, `"ל"` or `"ن"`, so the palette never opens. Browsers' own shortcuts use the physical key for this reason.
**Suggested fix**: for modifier shortcuts, match `event.code` (`KeyK`) when the key is a letter, falling back to `event.key` for symbols. Keep `event.key` for typed single characters such as `?`. Add a test with `key: "л", code: "KeyK"`.

### 🟡 22. `ConfirmDelete` count check and failure text, `console/src/ui/confirm-delete/index.tsx:85, 87`

**Problem**: `typed.replace(/\D/g, "") === String(count)` strips every non ASCII digit and every non digit, so "1x0x0x1" confirms 1001 rows, and in Arabic or Persian digits the user can never match. The failure text, "Check your connection and try again", is also shown for a `conflict` or `forbidden` failure, where that advice is wrong. UX-22 wants the failure's kind to pick the wording.
**Suggested fix**: parse with the active locale's digits (normalise to ASCII, then compare the whole trimmed string), and render the error with `InlineError` (or `useErrorCopy`) so the kind and request ID show.

### 🟡 23. Unsaved changes: object valued search params always count as changed, `console/src/ui/form/unsaved-changes.ts:72-80`

**Problem**: `affectedBy` compares watched params with `!==`. A param that parses to an array or object (a filter) is a new object on every location, so any navigation reports the form as affected and asks the discard question even when the param did not change.
**Suggested fix**: compare with a stable serialisation (`JSON.stringify`, already used for `watchKey` in the same file).

**Theme and styles**

### 🟡 24. `prefers-contrast: more` loses to the OS dark fallback, `console/src/ui/theme.css:197-237`

**Problem**: THM-6 sets `--color-border` and `--color-fg-muted` on `:root` inside `@media (prefers-contrast: more)`. The no script dark fallback is `:root:not([data-theme])` inside `@media (prefers-color-scheme: dark)`, which has higher specificity (0,2,0 against 0,1,0), so in that path (script blocked, OS dark, high contrast on) the dark `border` and `fg-muted` win and the contrast request is ignored. With `data-theme` set, the same specificity and later order make it work, which is why the boot test passes.
**Suggested fix**: write the contrast block as `:root, :root:not([data-theme])` or order it with the same selector as the dark block. Add a test that disables `theme-boot.js`, emulates dark plus `prefers-contrast: more`, and checks `--color-border`.

### 🟡 25. Transitions animate colors when the theme changes, `console/src/ui/popover.tsx:26`, `console/src/ui/dialog.tsx:58`, `console/src/ui/command-palette/index.tsx:53`

**Problem**: THM-5 says a theme switch never animates colors. `popover.tsx`, the dialog `Modal` and the palette `Modal` use Tailwind's `transition`, which includes `background-color`, `border-color` and `color`. The account menu is a popover, and it is the control that changes the theme: the popover is still on screen (exiting) when `data-theme` flips, so its background fades over 200 ms.
**Suggested fix**: name the properties: `transition-[opacity,scale]` (or `transition-[opacity,transform]`) with the same duration and easing, in all three places, and add a theme switch test that reads `getComputedStyle(popover).transitionProperty`.

### 🟡 26. Dialog and sheet titles are one size larger than the tokens say, `console/src/ui/dialog.tsx:63`, `console/src/ui/side-sheet/index.tsx:63`, `console/src/ui/docs-pane/index.tsx:120`

**Problem**: `theme.css` and `design.md` _Typography_ give `--text-md` to "Section headings (h3), sheet and dialog titles" and `--text-lg` to "Card group headings (h2)". `Dialog`, `SideSheet` and the docs pane heading use `text-lg`. Either the tables or the primitives are wrong, and the generated table in `design.md` now misleads.
**Suggested fix**: pick one (a 20 px dialog title is plausible) and change the other, then run `pnpm fix`.

**Tooling, CI and tests**

### 🟡 27. The forced colors rule adds a border to every selected element, `console/src/ui/theme.css:241-247`

**Problem**: `[data-selected] { border-inline-start: 2px solid Highlight }` matches any React Aria `data-selected` element: checkboxes, radios, switches, listbox options, tabs, and rows. A 2 px border changes the size of a 16 px checkbox hit box and shifts layout, but only in forced colors mode, which is exactly where nobody tests by eye. AC-6 only asserts the sidebar item and the current tab.
**Suggested fix**: scope it to the selectors AC-6 names (`nav [aria-current="page"]`, `[role="tab"][aria-selected="true"]`, `[role="row"][data-selected]`) and use `outline` or `box-shadow: inset` so nothing moves.

### 🟡 28. Lint rules leave the easy bypasses open, `tooling/config/console-restrictions.ts:62-73, 256-274`

**Problem**: (a) SEC-2 names `dangerouslySetInnerHTML`, `eval`, `new Function`, and event handler strings, but not `innerHTML`, `outerHTML`, `insertAdjacentHTML` or `document.write`, which are the more common way to write raw HTML. (b) STY-5's arbitrary value pattern is `-\[` or `^\[name:`, so an arbitrary variant such as `[&_svg]:size-4` or `[@media(x)]:p-2` passes outside `src/ui`. (c) `classStrings` looks only at `className` literals and `cx(...)` arguments, so a class string in a constant or an object value (`const wide = "w-[13px]"`) passes, and RTL-2's template check covers only `className` templates, not ``cx(`ps-${x} left-0`)``.
**Why it matters**: these rules are the enforcement for AC-19 and the CSP story; each missing path is a lint pass that should have failed. Rules: STY-5, RTL-2, SEC-2.
**Suggested fix**: add the four HTML sinks to SEC-2 with fixtures; widen the arbitrary pattern to `(^|\s)(\S*:)?\[` ; add `TemplateLiteral` quasis inside `cx(...)` to both class string selectors. Each gets a fixture under `tooling/lint-fixtures/`.

### 🟡 29. The CSP is copied into `vite.config.ts` with no equality test, and the CSP test only loads pages, `console/vite.config.ts:9-13`, `console/e2e/csp.spec.ts`

**Problem**: the policy string is duplicated from `SecurityHeaders.cs` ("the exact policy"), and nothing compares them. The strings match today. If the server adds a directive, `vite preview` keeps testing the old policy and the e2e stays green (STY-6). The test also only navigates to each route and waits for `main`; it never opens a menu, dialog, tooltip or toast, which is where inline `style` attributes or injected `<style>` elements would be blocked.
**Suggested fix**: add a tooling test that parses the policy out of `SecurityHeaders.cs` and compares it with `vite.config.ts` (or have `vite.config.ts` read it from one JSON shared with the server test). Extend the e2e through the production build with one scripted interaction that opens the account menu, a dialog (the welcome page can host a tiny test only route, or use `/` plus a keyboard Mod+K that opens the palette) and a toast, and still expects zero violations.

### 🟡 30. The gallery check inspects only manifest entries, `tooling/scripts/check-gallery-not-shipped.mjs:20-25`

**Problem**: it fails when `gallery.html` exists or a manifest chunk's `src` is under `src/gallery/`. The manifest lists entry and dynamic chunks, not every bundled module. A gallery module imported from shipped code (lint forbids it, but a relative path through a barrel is only `@/gallery` shaped by luck) would be bundled into a normal chunk and pass.
**Suggested fix**: also scan `dist/assets/*.js` for a marker string the gallery always includes (a `// gallery` banner in `gallery/main.tsx` or the fixtures' sample org names), and keep the manifest check.

### 🟡 31. The skeleton timing test has a 50 ms margin on real timers, `console/src/ui/skeleton.test.tsx:40-47`

**Problem**: "a load that ends after 250 ms never shows the skeleton" runs on real timers in three engines on a shared CI runner. A 50 ms stall between the load timer and the hook's 300 ms timer (a slow WebKit start, GC, the 400 ms wait after) shows the skeleton and fails the test for a reason unrelated to the hook. The spec asks for real timers (AC-17), so only the margin can change.
**Suggested fix**: keep real timers for the second test; for the first, use a load of 100 ms (the point of the test is "before the delay"), and keep the `>= 800 ms` assertion as it is (it is a lower bound, which cannot flake).

### 🟡 32. The size budget has little room and measures an empty shell, `console/scripts/size-entries.mjs:75-102`

**Problem**: the probe renders `AppShell` with no nav items, an empty search, and one page. Real shell routes add the nav registry's icons, the API client, TanStack Query, the real switcher data hooks and the first translated catalog. The first load was 218.72 KB of 230 at the last verify. `size-limit` sums each file's own gzip, which is slightly more than one stream, so the number is conservative, but there is still no per package breakdown in CI.
**Suggested fix**: print the largest ten modules of the shell chunks (a `rolldown` visualizer run in the size step, as an artifact) so the next 5% is a decision; fix 2 should give back several KB. Re run the probe with a representative nav (all eight services) when feature 13 adds routes.

### 🟡 33. `ExternalLink` trusts its `href`, `console/src/ui/link.tsx:94-109`

**Problem**: today the only caller passes the constant `DOCS_FALLBACK_URL`, but the component is exported and typed `href: string`. A `javascript:` or `data:` value from a future caller (a docs link from the API) would be rendered as is. `rel="noopener noreferrer"` and `target="_blank"` are right.
**Suggested fix**: allow only `https:` and `http:` (`new URL(href).protocol`), render plain text otherwise, and add a test.

## Nit

- **34**. `stored.ts:95-97`: the `storage` listener matches on key only. `localStorage.clear()` in another tab sends `key === null` and a `sessionStorage` event with the same key also matches, so the stored item and the store disagree. Check `event.storageArea === localStorage` and treat a null key as "reset to fallback".
- **35**. `shared/stores/layout.ts:17-20`: each `storeFor(userId)` subscribes to storage and never unsubscribes. Harmless for one user, but a sign out and sign in as another leaves the old store listening.
- **36**. `ui/avatar.tsx:10`: `Array.from(text)[0]` splits by code point, so an emoji with a joiner or a letter with a combining mark gives half a character. `Intl.Segmenter` gives the first grapheme.
- **37**. `shared/format/index.ts:55-57`: `Math.round` makes 59.6 minutes "60 minutes ago" and 23.6 hours "24 hours ago". Floor the unit (or round only inside the unit) so the step to the next unit happens at the boundary.
- **38**. `ui/toast/index.tsx:46-55`: an identical toast only bumps a count. The timer is not restarted and nothing is re announced, so a repeated "Copied" is silent the second time and the first toast may close under the user.
- **39**. `ui/shell/activity-indicator.tsx:1003-1006`: `seen` is set once when the popover opens, so messages that arrive while it is open show the dot again after it closes.
- **40**. `ui/confirm-delete/dependents.ts:50`: for an unknown kind the user sees the raw API name ("3 of kind foo_bar"). Fix with the `in` bug above: look up with `Object.hasOwn` and humanise the fallback (`foo bar`).
- **41**. `ui/shell/breadcrumbs.tsx:139-146, 85-95`: a page crumb with no link becomes a menu item that does nothing, and an org or project crumb in the last position never gets `aria-current="page"` (SRT-4 says the last crumb is current).
- **42**. `ui/skeleton.tsx:83-85`: `aria-busy="true"` wraps only hidden "Loading" text. Many screen readers skip content inside a busy element, so the text may not be read; put `aria-busy` on the region being loaded (the caller's container) and keep the text.
- **43**. `ui/form/field.tsx:121-175`: the custom `Field` declares `isDisabled` and `isReadOnly` through `FieldProps` and ignores them.
- **44**. `ui/shell/app-shell.tsx:205`: focus returns to the help button with `querySelector("button")` on a wrapper `span`; a ref on the button itself (through `IconButton`) is sturdier.
- **45**. `ui/shell/drawer.tsx:307-311`: the `widths` map wraps each class in `cx(...)` for no reason, since `cx` with one literal returns it. Plain strings are the same for Tailwind's scanner.
- **46**. `ui/use-shortcut.ts:75`: the handler does not skip `event.repeat`, so holding Mod+K calls `trigger()` repeatedly (harmless for the palette, wrong for a toggle).

## Already known (not counted)

- `confirm-delete/dependents.ts:48` uses `in`, so a `toString` or `constructor` kind renders as `[object Object]`. Pinned by `it.fails` in `confirm-delete/dependents.test.ts:64`. Nit 40 is a separate point (the fallback text) that the same fix should cover.
- `Banner`, `InlineError` and `EmptyState` do not wrap long words at 320 px (AC-8). Pinned by three `it.fails` tests in `feedback.test.tsx:364-376`. Finding 3 is a different component (`Dialog`).
- AC-13 says listbox but PRM-8 says menu, and AC-12's live region list leaves out `InlineError`: for `/architect`. Findings 11 and 20 ask for one or two more live regions, so those two spec edits should be done together.
- AC-22, the screen reader runs, is owed to a person. Findings 7, 11 and 17 add steps worth including in SRT.

## What I checked and found sound

- The `useShortcut` registry, the Mod key handling, the modal guard (`data-modal`), and `isTyping` are correct and the shortcut list cannot drift from what is bound.
- `UnsavedChangesGuard`, `confirmDiscard`, and the interaction lock count and release holders correctly; the guard is the only user of `useBlocker`, and the lint rule has a fixture.
- Storage (`stored.ts`) never throws, falls back to memory, and drops a bad entry; the theme boot script is a classic file, runs in a `try`, and agrees with the store's `resolve`.
- `Toast` never takes focus, errors stay until dismissed, the history keeps 50, and `--toast-inset` is cleaned up.
- The palette runs the chosen action only after focus has returned, and recents ignore ids that left the scope.
- `CopyField` and `SecretReveal` keep the secret out of every store, announce once, and fall back to selecting the text.
- The restriction rules are built one region at a time with no inline disables (LNT-3), and the `packages()` regex is anchored.
- `_console.yml` has `permissions: {}`, every action pinned to a full SHA, `persist-credentials: false`, and a job timeout; the Playwright and apt caches are keyed by version.
- CSP: no `dangerouslySetInnerHTML`, no `eval`, the React Aria `<style>` is suppressed by the template placeholder and the rule moved to `theme.css`, and style writes go through the CSSOM.

## Not covered

- Nothing was executed. No test, build, size check, gallery or screen reader run was done for this review.
- The gallery scenes and fixtures, `console/design.md` prose, the `.po` catalogs, and the generated route tree were not reviewed beyond what the findings cite.
- `tooling/scripts/design-tokens.mjs` was read for its parser only. A declaration with no trailing semicolon before `}` would be dropped, but `theme.css` has none.
