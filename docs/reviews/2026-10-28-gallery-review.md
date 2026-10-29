# Gallery review, design system and console shell (spec 0015), 2026-10-28

A separate pass over the dev gallery (`console/gallery.html`, `src/gallery/`) on a fresh model (Sonnet 5.5), collecting suggestions and updates. It is a report only: no code changed. It feeds the later triage next to the review in [2026-10-27-review-design-system-console-shell.md](2026-10-27-review-design-system-console-shell.md), whose items are cited here as **R-n** (for example R-13) and are not repeated.

## Scope and method

- Built from `origin/main` at `75e9fbf`, served with `vite` on port 5182, walked in the built-in browser. Every route in `src/gallery/router.tsx` (17 hash routes: the index, 5 primitive scenes, 2 plain error pages, `AuthLayout`, and the shell scenes in account, org, and project scope with and without a resource list and a table open).
- Conditions covered: light and dark, comfortable and compact, left to right and right to left (`ar-XB`, the toolbar's only right to left option), English and the pseudo locale, and widths 320, 874, 1024 and 1280 px, plus the toolbar's 800 px frame. The viewport was reset at the end and the gallery's local storage cleared.
- Keyboard: tab order through the whole shell scene, focus rings, Escape and focus return on the switchers, activity popover, help pane, account menu, palette, all dialogs and `ConfirmDelete` variants, separator keys, tab arrows, and the nav drawer at 320 px.
- Checked mostly with `read_page`, `get_page_text` and small DOM probes; screenshots only for visual findings, none committed.
- Not done: a screen reader (AC-22 stays owed), hover and pressed states (the gallery shows none statically), forced colors, reduced motion, 400% zoom, Firefox and WebKit, and the real `ar` locale (the gallery cannot select it, see G-5).
- Console: no errors or warnings. The only messages are Vite's debug lines, the React DevTools hint, and Lingui's dev-only "I18nProvider rendered `null`" notice, logged twice before a locale is active.
- Findings are numbered G-1 onward, ranked by impact inside each band.

## Summary

The gallery is in good shape. Nothing was broken in a way that blocks release: every overlay opens, traps, closes on Escape and returns focus; the shell scenes have one `h1`, one `main`, descriptive titles, and a logical tab order with a visible 2 px ring; no scene scrolls the page sideways at 320 px; and focus lands on Cancel for tier 2 deletes.

The things worth fixing first:

1. At 320 px the top bar crushes the breadcrumbs: the organization crumb loses its name and is a 16 px wide button, and the last crumb shows one letter (G-1, AC-8, WCAG 2.5.8).
2. The pseudo locale only covers the shell's own chrome and does not grow text, so UX-34's 30% growth cannot be seen on any scene (G-2, G-3).
3. The gallery cannot reach several states `design.md` lists: switcher loading and error, list loading, empty and filtered empty, the dirty and pristine `SettingsCard`, hover, pressed, and the real `ar` locale (G-4 to G-6).
4. `Kbd` keys reverse in right to left (`K ⌘`), and truncated Latin names lose their start in right to left (G-7, G-8).

| Band         | Count |
| ------------ | ----- |
| High         | 2     |
| Medium       | 9     |
| Low          | 12    |
| Nice to have | 7     |

## High

### G-1. At 320 px the breadcrumbs are unusable and the organization switcher is a 16 px target

- **Where**: project shell scenes (`#/acme/storefront`, `#/acme/storefront/databases/users`), top bar, `src/ui/shell/breadcrumbs.tsx` and `app-shell.tsx`.
- **Condition**: any theme and density, left to right, English, 320 px wide (the width AC-8 names); 360 px in the toolbar frame is likely similar but was not measured.
- **Saw**: the bar holds seven 32 px controls (nav button, brand mark, crumbs, activity, help, palette icon, account). What is left for the crumbs is about 70 px. The organization crumb ("Acme") has no visible text and its switcher button measures 16 by 32 px, so it is below the 24 px minimum (WCAG 2.5.8, design.md principle 4). The page crumb is cut to "O." for "Overview". The ellipsis button ("Show the full path") works, but the current page's name cannot be read at all.
- **Change**: below a threshold (about 400 px) let the crumbs wrap to a second row, or show only the current page crumb with the ellipsis menu, and give the org switcher a minimum 24 px by 24 px target. Moving the activity button into the account menu below 400 px would also free 36 px. Add a 320 px assertion to the shell tests (width of the org button, the last crumb's visible text).
- **Relates to**: AC-8, UX-34, design.md _Shell_ (Breadcrumbs: "Below 64rem every crumb except the first and last collapses into a menu"; the last crumb must stay readable) and principle 4.

### G-2. The pseudo locale does not grow text, so UX-34 cannot be seen anywhere

- **Where**: toolbar _Locale_ → _Pseudo_, every scene; `lingui.config.ts` (`pseudoLocale`).
- **Condition**: pseudo locale, any theme, width, density.
- **Saw**: the shell chrome is accented ("Ōvēŕvĭēŵ", "Śèàŕćĥ ṕŕǿĴèćţŝ", "Ćōḿḿàńďś⌘K") but each string keeps its English length. Nothing is lengthened by 30% or more, which is what "text containers allow 30% growth" (design.md Typography, UX-34) needs in order to expose clipping, wrapping and fixed widths. The pane at 320 px only shows breakage (G-1) because the English text is already too long.
- **Change**: generate the pseudo catalog with padding (for example wrap each message in brackets and append about 35% filler, keeping placeholders intact), or add a toolbar "Grow text" switch that applies the same to the active catalog. Then add one scene check per component family at 320 px and at the longest strings.
- **Relates to**: design.md _Typography_ (UX-34), spec 0015 GAL-2 and AC-20, spec 0003 I18N-7.

### Note on G-1 and G-2 together

Run G-1's fix with G-2's padded locale on: the top bar, tabs, `PageHeader` action, and `RouteTabs` will all show clipping that English alone does not.

## Medium

### G-3. Scene copy never goes through the pseudo locale, so most text on a scene is not tested

- **Where**: all scenes except the shell chrome (for example `#/acme/storefront/databases/users`, "Rows", "Columns", "Insert row", "Tables", "A table in the Storefront database.").
- **Condition**: pseudo locale.
- **Saw**: with Pseudo selected only the primitives' own strings are accented. Scene copy is plain English because GAL-3 keeps `src/gallery` out of the catalog and `<html lang>` is set to the invalid tag `pseudo-LOCALE`. So tabs, headers, buttons and list headings on the scenes stay English, and truncation, wrapping and RTL mirroring of those labels are not exercised.
- **Change**: let the gallery run its own scene strings through a small `pseudoize()` helper that reuses the same transform as the catalog (G-2) so the check covers scene text without entering the production catalog. Set `lang` to a valid tag (for example `en-XA`) when the pseudo locale is on.
- **Relates to**: GAL-3, AC-20, I18N-7.

### G-4. The gallery cannot show the switcher, list and form states that design.md lists

- **Where**: `OrgSwitcher`, `ProjectSwitcher`, `ResourceList`, `SettingsCard`, `Select`, `Combobox`; `src/gallery/fixtures.ts`.
- **Condition**: any.
- **Saw**: the fixtures always succeed instantly. Missing from the gallery: switcher _loading_, _error with Retry_ (design.md _Shell_ lists both), `ResourceList` _loading_, _empty_ and _filtered empty_ (the filter works but the filtered empty text is only seen by typing; loading and empty cannot be reached), `Combobox` _loading_ and _empty_, `Select`, `NumberField`, `PasswordField`, and `TextField` in _invalid_, _read only_, _disabled_ without submitting (the only invalid state is reached by pressing "Create bucket" on the empty form), `ErrorSummary` before a submit, and `ActivityIndicator` with running jobs. The "Overlays" scene also has a "Tier 1, loading" case but nothing for loading `ResourceLayout`.
- **Change**: add a small _Scenario_ switch beside the toolbar (or per scene) with "Slow", "Fail", "Empty" for the fixtures, and add a static "States" scene for the field family (default, focus, invalid, read only, disabled, with help text) so each state can be seen and reviewed without interaction.
- **Relates to**: GAL-2 ("every primitive in every variant and state"), AC-20, design.md _Forms_, _Shell_ (switchers), _Components_ rows "loading, empty, error".

### G-5. The direction switch uses `ar-XB`, which the repo's own notes say is unreliable

- **Where**: toolbar _Direction_ → "Right to left (ar-XB)"; `src/gallery/settings.ts`, `providers.tsx`.
- **Condition**: right to left.
- **Saw**: `console/AGENTS.md` (Tests) says CI's WebKit resolves `ar-XB` as left to right and tests use the real `ar` locale. The gallery and design.md (_Right to left_) still describe `ar-XB` as the gallery's right to left locale and RTL-1 names it. In Safari or a WebKit browser the gallery's right to left mode may flip layout (`dir="rtl"`) but not React Aria's keyboard handling, so a person reviewing there would see a half mirrored result. The task also asks for the `ar` locale and the toolbar has none.
- **Change**: offer both: "Right to left (ar)" using the real locale, and keep "ar-XB" as a second option, or switch the gallery to `ar` and note why. See the spec changes list.
- **Relates to**: RTL-1, AC-18, design.md _Right to left_, `console/AGENTS.md` Tests.

### G-6. No way to inspect hover, pressed, forced colors, reduced motion, or zoom

- **Where**: toolbar; `#/primitives`, `#/feedback`.
- **Condition**: any.
- **Saw**: design.md's first three principles promise tests for "forced colors, reduced motion, 400% zoom", and the _Button recipes_ table defines hover and pressed colors. The gallery shows every button in rest, unavailable and disabled only. Skeletons, the running chip's spinner, and the toast timers can only be seen at full motion. The toolbar has no motion, contrast, or zoom switch.
- **Change**: add a "State" column to the button scene that forces `data-hovered`, `data-pressed` and `data-focus-visible` (React Aria exposes these attributes), and a toolbar group "Preview" with reduced motion, `prefers-contrast: more`, and forced colors emulation (a data attribute that mirrors the media rules, or a note telling reviewers to use the browser's rendering panel). A 200% / 400% zoom shortcut is nice to have.
- **Relates to**: design.md Principles 3, _Button recipes_, _Motion_, _Themes_; MOT-1, THM, AC-15.

### G-7. `Kbd` reverses its keys in right to left

- **Where**: `PaletteButton` in the top bar, `Kbd` on `#/primitives`, `ShortcutHelp`.
- **Condition**: right to left, any theme, width at least 1024 px.
- **Saw**: the outer `kbd` inherits `direction: rtl`, so "⌘" and "K" render as "K ⌘". A shortcut is a physical sequence, not language, and reads wrong when reversed.
- **Change**: set `dir="ltr"` (and `unicode-bidi: isolate`) on the outer `Kbd`. Add it to the RTL browser test (the keys' DOM order should match their visual order).
- **Relates to**: design.md _Right to left_ and _Foundations_ (`Kbd`), AC-18, RTL-1.

### G-8. In right to left a truncated Latin name loses its start

- **Where**: `ResourceList`, the long name `audit_log_entries_with_a_very_long_name_that_truncates`.
- **Condition**: right to left, any theme, 1024 px and wider.
- **Saw**: the row shows "…_name_that_truncates": the visible part is the end of the identifier and the leading "audit_log" is dropped. Latin identifiers and IDs are common in right to left UIs, and the start is what people recognise.
- **Change**: render user data (names, IDs, keys) in an isolate with `dir="auto"`, or `<bdi>`, so the truncation ellipsis lands at the end of the text in its own direction; keep the full name as the accessible name and tooltip (UX-34 already requires the tooltip). Add an RTL case with a long Latin name to the fixtures.
- **Relates to**: design.md _Typography_ ("truncation always keeps the full text as the accessible name plus a tooltip"), _Right to left_, RTL-1.

### G-9. The switchers do not show which item is current

- **Where**: `OrgSwitcher`, `ProjectSwitcher` (`src/ui/shell/switcher.tsx`), opened from the top bar.
- **Condition**: any theme, density, direction.
- **Saw**: opening the project switcher on "Storefront" lists Storefront, Billing, Analytics, Mobile app, then "All projects" and "New project". The current project has no check, no selected style and no `aria-current`/`aria-selected`, and the highlighted row after open is "All projects". A screen reader user opening the list cannot tell which project they are in.
- **Change**: mark the current item (check icon plus `aria-current="true"` or selected state) and start the highlight on it. Add the step to SRT-4 ("hear the current project").
- **Relates to**: design.md _Shell_ (Switchers), SHL-12, SRT-4.

### G-10. `AuthLayout` scene has no fields, so its large controls and its copy are untested

- **Where**: `#/auth-layout`, `src/gallery/scenes/auth-scene.tsx`.
- **Condition**: any.
- **Saw**: the card holds "Sign in to Orvano", the sentence "Forms arrive with milestone 3; this shows the frame and the card." and a single Sign in button. Milestone 3 has long shipped, so the copy is stale. design.md says auth pages use "large (40 px) controls" but no `TextField` or `PasswordField` in `lg` is shown anywhere, and `size="lg"` for fields is not in the Forms scene either.
- **Change**: put a real sign in form in the card (email, password, a `lg` Sign in button, a link row, an `InlineError` variant) with the `lg` sizes, and delete the milestone sentence. Add a boot-error and root-error check at 320 px for the Request ID field.
- **Relates to**: design.md _Shell_ (Pages without the shell), _Spacing_ (`--spacing-control-lg`), AC-21.

### G-11. `SettingsCard` "Update" is enabled with nothing changed

- **Where**: `#/data`, SettingsCard "General".
- **Condition**: any.
- **Saw**: the card holds a single read only field ("Project ID"), yet the "Update" button is primary and enabled (`disabled` is false, no `aria-describedby`). design.md says it is "enabled only when changed (FRM-5)". Either the scene cannot show the rule, or the card doesn't enforce it.
- **Change**: give the scene an editable field so the pristine and dirty states show, and make sure the button is unavailable with a reason until the form is dirty (or confirm that the prop is the caller's job and say so in design.md).
- **Relates to**: design.md _Data display_ (`SettingsCard`), FRM-5.

## Low

### G-12. Every gallery page shares one document title

- **Where**: `/`, `#/primitives`, `#/forms`, `#/overlays`, `#/feedback`, `#/data`, `#/boot-error`, `#/root-error`, `#/auth-layout`.
- **Saw**: `document.title` is "Orvano gallery" on all of them; the shell scenes correctly build "Overview · Storefront · Orvano". Tabs and history entries are indistinguishable, and a screen reader announces the same title on every scene.
- **Change**: set `Primitives · Gallery`, `Forms · Gallery`, and so on in `GalleryFrame`, reusing `useDocumentTitle`. WCAG 2.4.2 applies in spirit; it is a dev page.
- **Relates to**: UX-4, design.md _Components_ (`useDocumentTitle`).

### G-13. Scroll position carries over between scenes

- **Where**: toolbar `GalleryFrame`, the `div.overflow-auto` around `<Outlet />`.
- **Condition**: any.
- **Saw**: opening `#/feedback` from a page scrolled to the bottom shows the new scene already scrolled partway. Reviewers have to scroll up each time.
- **Change**: scroll the scene container to the top on route change (or key it by pathname).
- **Relates to**: GAL-1.

### G-14. Gallery state lives in the browser's storage and survives reloads

- **Where**: `orvano:v1:theme`, `orvano:v1:usr_ada:layout`, per service resource list width, recents.
- **Saw**: a stored density, theme and list width (`width: 200` after a Home key press) carry into the next session, so the same URL shows different layouts to different reviewers, and the separator's value read 216 on first open after an earlier narrow session.
- **Change**: add a "Reset gallery" button to the toolbar that clears the `orvano:v1:*` keys and reloads. Useful before every screenshot.
- **Relates to**: GAL-1.

### G-15. The toolbar uses about 40% of a 320 px screen, and sits before the skip link in the tab order

- **Where**: `GalleryFrame`, toolbar `role="toolbar"`.
- **Condition**: width below about 500 px.
- **Saw**: the six controls stack one per row, about 265 px, leaving roughly 430 px of a 700 px high viewport for the scene. Tab order starts in the toolbar (Theme, Density, Locale, Direction, Width) and reaches "Skip to content" only after, so the gallery does not show SRT-1's first Tab behaviour. A `role="toolbar"` also expects arrow key movement, which these native selects do not give.
- **Change**: lay the toolbar out in two columns below 640 px or collapse it behind a "Settings" disclosure; render it after the scene in DOM order but above visually, or add its own skip link; drop `role="toolbar"` (a plain `group` with a label fits native selects).
- **Relates to**: GAL-2, SHL-3, SRT-1.

### G-16. The 360 px and 800 px frames reload on every navigation, and 320 is missing

- **Where**: toolbar _Width_, `GalleryFrame` (`iframe` keyed by `frameUrl(...)`).
- **Saw**: choosing a different scene while a width frame is on remounts the iframe, which loses scroll, open overlays and storage reads. The options are 800 and 360 px, but AC-8 names 320 px, the width where G-1 and R-3 show up. A navigation inside the frame was not checked against the outer hash.
- **Change**: add a 320 px option and change the frame's hash through `postMessage` or `contentWindow.location.hash` instead of remounting; confirm that a link followed inside the frame updates the outer URL.
- **Relates to**: GAL-2 ("a narrow width frame"), AC-8.

### G-17. Standalone external link is below 24 px high

- **Where**: `#/primitives`, ExternalLink "Open the Orvano docs"; also the docs pane's link.
- **Saw**: the link box is 20 px high. WCAG 2.5.8 exempts links inside a sentence, but this one stands alone on its own line.
- **Change**: give a standalone `ExternalLink` a 24 px minimum height (padding block 2 px) or a size variant, as `Button` has.
- **Relates to**: design.md Principle 4.

### G-18. Table rows at 320 px: horizontal scrollbar covers the last row, and the key breaks mid-text

- **Where**: `#/data`, "Table, sortable" and "Table, selectable".
- **Condition**: compact density, 320 px, dark (the same in light).
- **Saw**: the table scrolls inside its own container (good, the page does not scroll), but the overlay scrollbar sits on the last row's text ("Old server" is half hidden), the container has no label, and the masked key `ok_live_7f3a…91c2` breaks onto two lines at the ellipsis, so the row grows from 28 to 42 px. "Created" is clipped at the edge with no hint of more columns.
- **Change**: add bottom padding equal to the scrollbar to the scroll container, keep masked keys on one line (`whitespace-nowrap`), and give the region an accessible name so its scrolling is announced. Relates to R-15 for the sticky header.
- **Relates to**: UX-34, design.md _Data display_ (`Table`), AC-8.

### G-19. Toast announcements may be doubled and use the assertive role for success and info

- **Where**: `#/feedback`, `notify` buttons; `src/ui/toast/index.tsx`.
- **Saw**: each toast is an `alertdialog` that contains a separate `role="alert"` node (React Aria's markup). `alert` is assertive, including for `success` and `info`, and the history shows the same text in two live nodes. Not heard with a screen reader, so unverified.
- **Change**: during SRT-8, listen for double announcement of one toast and for an interrupted announcement on success. If either shows, use a polite announcer for `success` and `info`, and give the toast the single live region AC-12 allows.
- **Relates to**: AC-12, A11Y-4, PRM-8, SRT-8; see also R-38 (repeated toast).

### G-20. A palette action appears twice

- **Where**: command palette, empty query, `#/acme/storefront/databases/users`.
- **Saw**: "Go to Databases" is listed under "Recent" and again under "Go to" a few rows below.
- **Change**: remove recents from the main groups while the query is empty (the recent entry moves, it is not copied), or label the group "Recent" and skip duplicates.
- **Relates to**: SHL-10, AC-13.

### G-21. Banner scene uses one sentence for five tones

- **Where**: `#/feedback`, "Banner".
- **Saw**: Neutral, info, success, warning and danger all say "Storefront is at 90% of its database storage". A success or neutral banner with that text reads wrong, and a danger banner at 90% is not an error. Only the warning one has an action ("Upgrade plan").
- **Change**: give each tone its own realistic message ("Backup finished", "Maintenance on Sunday", "Storage is full", and so on) and show one banner with an action in every tone.
- **Relates to**: design.md _Writing in the UI_, _Feedback and states_ (`Banner`).

### G-22. Forms scene copy and field details

- **Where**: `#/forms`, "A form with every field".
- **Saw**: (a) the label "Maximum file size (MB) (optional)" has two parentheses back to back; (b) the empty password field fails with "Use at least 8 characters." where an empty required field should say "Enter a password"; (c) "Owner (optional)" is a combobox with no placeholder while "Region" shows "Choose a region", so the two look different when empty; (d) the show/hide toggle sits outside the input, 12 px off the edge, so the password field is narrower than the others and its border and toggle do not line up with the column; (e) a failed submit lists 3 problems but only 2 controls get `aria-invalid` (the Region trigger is not marked).
- **Change**: write the unit in help text ("In MB") instead of the label, add an empty check to the password schema, give optional comboboxes a placeholder, place the toggle inside the field's end, and set `aria-invalid` on the `Select` trigger.
- **Relates to**: design.md _Forms_, UX-7, UX-8, FRM-2.

### G-23. Placeholder product names are pseudo localized

- **Where**: docs pane (Help) in the pseudo locale: "Open the Ōŕvàńō docs", "Ōŕvàńō documentation is on ĜĩţĤũb".
- **Saw**: the brand and product names are inside the translated sentences, so they get accented. design.md says brand and product names stay untranslated (Writing in the UI, I18N-4), and real translators will see "Orvano" and "GitHub" as part of the string.
- **Change**: pass names as placeholders (`{product}`), so they are not translated or pseudo localized.
- **Relates to**: design.md _Writing in the UI_, I18N-4.

## Nice to have

### G-24. Heading levels inside scenes are flat

- **Where**: `#/data`, `#/feedback`, `#/overlays`.
- **Saw**: scene sections, `SettingsCard` titles ("General", "Danger zone") and `EmptyState` titles ("No webhooks yet") are all `h2`, with the danger zone rows as `h3`, so the outline cannot tell a demo section from the component's own title. design.md assigns h2 to "card group headings" and h3 to section headings.
- **Change**: demo section headings in the gallery frame could be `h2` and the components' own titles one level lower, or give the scenes a label that is not a heading.
- **Relates to**: design.md _Typography_ (scale uses), R-26.

### G-25. Nav and list items are one tab stop each

- **Where**: shell scenes; `ResourceList` (4 stops), the sidebar (11), breadcrumbs.
- **Saw**: Tab from the page start crosses about 34 stops before `main` unless the skip link is used. A real project list could have hundreds of tables.
- **Change**: keep links as native links (they are navigation), but consider a roving tab stop for `ResourceList` once a real list can be long, and mention "skip link also skips the resource list" in SRT-1.
- **Relates to**: UX-2, SHL-3.

### G-26. The mobile nav drawer has no visible close button

- **Where**: `#/acme/storefront` at 320 px, "Open navigation".
- **Saw**: the drawer is named "Project", traps focus, closes on Escape and returns focus to the opener (all good), but contains only the nav links, with no close control. Touch users must tap the scrim; the docs drawer does have one.
- **Change**: add a close `IconButton` at the drawer's inline end, as the docs pane has.
- **Relates to**: design.md _Shell_ (Drawer: "Labeled, traps focus, returns it to the opener"), SRT-11.

### G-27. Gallery lists "Rows, Columns" only

- **Where**: `#/acme/storefront/databases/users`.
- **Saw**: design.md's shell sketch shows "Rows Columns Indexes Permissions …". The scene has two tabs, so a tab bar that overflows (long labels, pseudo locale, 320 px) is never seen. `RouteTabs` has no overflow behaviour described at all.
- **Change**: add four to six tabs in the scene and decide on overflow (scroll with fade, or "More" menu) in design.md.
- **Relates to**: UX-5, design.md _Components_ (`RouteTabs`).

### G-28. Account scene is a placeholder

- **Where**: `#/account/profile` and the other account pages.
- **Saw**: "Placeholder content for the account scope" is all there is, so the account scope's page header, a `SettingsCard` with the real field sizes, and a Danger zone at page level are never shown inside the shell. The same applies to the org members and settings scenes.
- **Change**: reuse the `SettingsCard`, `Table`, and `DangerZone` scene content inside the org and account scenes, so spacing between sections (24 px, design.md _Page anatomy_) is verified in place.
- **Relates to**: design.md _Page anatomy_, GAL-2.

### G-29. The Switch demo has no "unavailable" explanation

- **Where**: `#/forms`, "Unavailable setting".
- **Saw**: the second switch is off and dimmed with no reason, while `Button` and `Menu` items explain themselves through `unavailableReason`. design.md's _Using color_ says disabled controls must "stay readable".
- **Change**: either document that `Switch` has no reason prop, or add one and show it. The scene name says "Unavailable" but the visual is the plain disabled style.
- **Relates to**: PRM-4, design.md _Using color_.

### G-30. Toasts cover content until dismissed

- **Where**: `#/feedback`, a stack of three toasts at 1280 px.
- **Saw**: the toast stack covers the "Upgrade plan" banner button until dismissed. Toasts never take focus (good) and `scroll-padding` handles a focused element (SHL-8), but a pointer user cannot click the covered button.
- **Change**: none required by the spec; success toasts close after 6 s. Worth noting in the gallery text that errors stay and cover content until dismissed.
- **Relates to**: SHL-8, UX-24.

## Gaps against design.md

- Shown in design.md but not in the gallery: switcher loading and error (G-4), `ResourceList` loading, empty and filtered empty (G-4), `ActivityIndicator` with running jobs (G-4), `NumberField` (G-22), a collapsed breadcrumb menu on its own, hover and pressed (G-6), the `lg` control size (G-10), `RouteTabs` with the four tabs the sketch shows (G-27).
- Shown in the gallery but not in design.md: the toolbar's 360 px and 800 px frames (design.md says "viewport hints"), the fixture names (`usr_ada`, `prj_…`), the cases of `ConfirmDelete` ("Tier 2, delete fails" and "Bulk, 1,200 rows"), and the hash routes themselves.
- design.md says the gallery's direction switch gives React Aria `ar-XB`; `console/AGENTS.md` says tests use `ar` (G-5).

## Spec or design.md changes suggested

For `/architect` or a docs edit, kept apart from the code changes above.

1. **RTL-1 and design.md _Right to left_**: decide between `ar` and `ar-XB` for the gallery, and say why, given AGENTS.md's note about CI WebKit (G-5).
2. **GAL-2 and AC-20**: add "320 px" to the width frames (AC-8 already names it), a "reset state" control (G-14), and the scenario switch and states scene for loading, error, empty, invalid, read only and disabled (G-4).
3. **GAL-2 / I18N-7**: say the pseudo locale must grow text by at least 30% (UX-34) and that scene strings go through it (G-2, G-3).
4. **design.md _Shell_ and AC-8**: state what the top bar does below 400 px (which controls stay, how crumbs behave) and a minimum target for the organization switcher (G-1).
5. **design.md _Foundations_ (`Kbd`)**: add "keys always read left to right" (G-7).
6. **design.md _Right to left_**: add a rule for user supplied text and IDs (`dir="auto"` or `bdi`, truncation at the end) (G-8).
7. **design.md _Shell_ (Switchers)**: add "the current item is marked and has `aria-current`" and a matching step in `verify.md` SRT-4 (G-9).
8. **design.md _Data display_ (`SettingsCard`)**: say whether "enabled only when changed" is enforced by the card or by the caller (G-11).
9. **design.md _Components_ (`Drawer`)**: say whether a drawer has a visible close control (G-26), and whether `RouteTabs` scrolls or collapses on overflow (G-27).
10. **verify.md**: add three steps for the next screen reader run: hear the current project in the switcher (G-9), one toast announced once for each of success, info and error (G-19), and the table region's name when it scrolls (G-18).
11. **design.md _Writing in the UI_ and I18N-4**: add "product names are placeholders in messages" so G-23 cannot come back.
12. **Gallery frame**: record in spec 0015 or in `src/ui/AGENTS.md` that every scene sets its own document title (G-12).

## What I checked and found sound

- Every gallery route loads without console errors or warnings; all scenes have exactly one `main` and one `h1`; there are no duplicate `id`s and no unnamed buttons, links, or fields (the only unnamed inputs are two hidden form inputs).
- Dialog, `SideSheet`, and all ten `ConfirmDelete` variants: focus moves in, Escape closes, focus returns to the opener; tier 2 and bulk variants start on Cancel; the alert dialogs have no description link (already R-7).
- Skip link shows with a solid 2 px focus ring and is the first stop in the shell. Focus rings read `oklch(0.673 0.182 277) solid 2px` in dark and inset on the tab links and on the separator.
- Resize separator: 16 px per arrow, Home and End at 200 and 480, named "Resize table list" with value, min and max (4 px wide, R-13).
- Docs pane opens beside the content from 1024 px, as a full width drawer at 320 px, from the left in right to left, and returns focus to the help button on Escape.
- No scene scrolls the page sideways at 320 px; the only overflow is inside the table's own scroll container.
- Dark and light surfaces use `bg-raised` plus `border` for toasts, menus, and the palette; chips and badges always show a label plus an icon or text.
