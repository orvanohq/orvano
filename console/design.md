# Orvano console design

The visual language of the Orvano console: what it looks like, the values behind it, and how each component behaves. The decision and the rules (STY, TKN, THM, MOT, SHL, PRM, ICN, RTL, GAL) are in [spec 0015](../docs/specs/0015-design-system-console-shell/index.md). Behavior is [spec 0001](../docs/specs/0001-console-ux-patterns/index.md) (UX rules) and code is [spec 0003](../docs/specs/0003-react-console-practices/index.md).

`src/ui/theme.css` is the only source of token values. The tables between `<!-- tokens:… -->` markers are generated from it by `pnpm fix`, and `pnpm check` fails when they are stale (TKN-6). To change a token, edit `theme.css` (the value, and the `/* use: … */` comment on the line above it) and run `pnpm fix`; never edit a generated table by hand.

To see everything live, run `pnpm dev` in `console/` and open `http://localhost:5173/console/gallery.html`.

## Principles

1. **Quiet by default.** Neutral surfaces, one accent, and color kept for meaning (status, danger, selection). Data and content are the loudest things on screen.
2. **Built from tokens.** Feature code never picks a color, font size, radius, or shadow; the utilities for anything off token don't exist.
3. **Accessible in every combination.** Light and dark, comfortable and compact, left to right and right to left, 400% zoom, forced colors, reduced motion. Each is tested, not hoped for.
4. **Dense, not cramped.** A 14 px base and 4 px spacing grid fit a lot on screen, but every target is at least 24 by 24 px and text never goes below 12 px.
5. **The same frame everywhere.** Top bar, sidebar, page header, content. Only the content changes.

## Color

All colors are OKLCH (lightness, chroma, hue). Neutrals are a cool zinc (hue 286), the accent is indigo (hue 277), and info is sky blue (hue 240), apart from the accent so "running" never looks like "selected". The light values sit on `:root`, the dark ones on `[data-theme="dark"]` (THM-3).

<!-- tokens:colors:start -->

| Token                      | Light                         | Dark                     | Use                                                                 |
| -------------------------- | ----------------------------- | ------------------------ | ------------------------------------------------------------------- |
| `--color-bg`               | `oklch(1 0 0)`                | `oklch(0.16 0.005 286)`  | Content area, inputs                                                |
| `--color-bg-subtle`        | `oklch(0.985 0.002 286)`      | `oklch(0.19 0.006 286)`  | Sidebar, top bar, resource list, auth page backdrop                 |
| `--color-bg-muted`         | `oklch(0.967 0.003 286)`      | `oklch(0.235 0.006 286)` | Hover rows, code blocks, skeletons, table header, disabled controls |
| `--color-bg-raised`        | `oklch(1 0 0)`                | `oklch(0.21 0.006 286)`  | Popovers, menus, dialogs, sheets, toasts                            |
| `--color-scrim`            | `oklch(0.21 0.006 286 / 0.4)` | `oklch(0 0 0 / 0.6)`     | Behind modal dialogs, sheets, drawers                               |
| `--color-tooltip`          | `oklch(0.21 0.006 286)`       | `oklch(0.967 0.003 286)` | Tooltip surface (inverted)                                          |
| `--color-tooltip-fg`       | `oklch(1 0 0)`                | `oklch(0.16 0.005 286)`  | Tooltip text                                                        |
| `--color-border`           | `oklch(0.92 0.004 286)`       | `oklch(0.3 0.008 286)`   | Decorative dividers and card edges                                  |
| `--color-border-strong`    | `oklch(0.6 0.015 286)`        | `oklch(0.57 0.015 286)`  | Input, checkbox, switch, and secondary button edges                 |
| `--color-fg`               | `oklch(0.21 0.006 286)`       | `oklch(0.967 0.003 286)` | Body text, headings, icons                                          |
| `--color-fg-muted`         | `oklch(0.46 0.016 286)`       | `oklch(0.72 0.015 286)`  | Help text, meta, placeholders, inactive nav, disabled text          |
| `--color-accent`           | `oklch(0.511 0.262 277)`      | `oklch(0.59 0.24 277)`   | Primary button, checked controls, progress                          |
| `--color-accent-hover`     | `oklch(0.457 0.24 277)`       | `oklch(0.511 0.262 277)` | Primary button hover and pressed                                    |
| `--color-accent-fg`        | `oklch(1 0 0)`                | `oklch(1 0 0)`           | Text and icons on `accent`                                          |
| `--color-accent-text`      | `oklch(0.511 0.262 277)`      | `oklch(0.785 0.115 275)` | Links, accent text on surfaces                                      |
| `--color-accent-subtle`    | `oklch(0.962 0.018 272)`      | `oklch(0.3 0.09 277)`    | Current sidebar item, selected rows, avatar                         |
| `--color-accent-subtle-fg` | `oklch(0.457 0.24 277)`       | `oklch(0.87 0.065 274)`  | Text on `accent-subtle`                                             |
| `--color-focus`            | `oklch(0.511 0.262 277)`      | `oklch(0.673 0.182 277)` | The focus ring                                                      |
| `--color-danger`           | `oklch(0.577 0.245 27)`       | `oklch(0.577 0.245 27)`  | Danger button                                                       |
| `--color-danger-hover`     | `oklch(0.505 0.213 27)`       | `oklch(0.505 0.213 27)`  | Danger button hover and pressed                                     |
| `--color-danger-fg`        | `oklch(1 0 0)`                | `oklch(1 0 0)`           | Text on `danger`                                                    |
| `--color-danger-text`      | `oklch(0.505 0.213 27)`       | `oklch(0.75 0.15 22)`    | Error text, inline errors, failed status                            |
| `--color-danger-subtle`    | `oklch(0.971 0.013 17)`       | `oklch(0.26 0.07 25)`    | Error banner, danger zone, failed chip                              |
| `--color-success-text`     | `oklch(0.48 0.13 150)`        | `oklch(0.79 0.17 152)`   | Success text, ready status                                          |
| `--color-success-subtle`   | `oklch(0.982 0.018 155)`      | `oklch(0.25 0.05 155)`   | Success banner, ready chip                                          |
| `--color-warning-text`     | `oklch(0.5 0.13 50)`          | `oklch(0.83 0.16 84)`    | Warning text, limits near                                           |
| `--color-warning-subtle`   | `oklch(0.987 0.022 95)`       | `oklch(0.26 0.05 70)`    | Warning banner and chip                                             |
| `--color-info-text`        | `oklch(0.48 0.12 240)`        | `oklch(0.78 0.12 233)`   | Info text, running status                                           |
| `--color-info-subtle`      | `oklch(0.977 0.013 236)`      | `oklch(0.25 0.04 240)`   | Info banner, running chip                                           |

<!-- tokens:colors:end -->

### Contrast

`src/ui/theme.contrast.test.ts` checks, in both themes (TKN-7):

- every text token (`fg`, `fg-muted`, `accent-text`, and the four status `-text` tokens) on every surface (`bg`, `bg-subtle`, `bg-muted`, `bg-raised`, `accent-subtle`, and the four status `-subtle` surfaces) at 4.5:1, except a status text on another status's surface;
- `accent-fg` on `accent` and `accent-hover`, `danger-fg` on `danger` and `danger-hover`, `accent-subtle-fg` on `accent-subtle`, and `tooltip-fg` on `tooltip` at 4.5:1;
- `border-strong`, `focus`, and `accent` on every surface at 3:1.

The tightest pairs today, all in the dark theme: `accent` on `accent-subtle` 3.05:1 (needs 3), the strong border on `accent-subtle` 3.11:1 (needs 3), and white on `accent` 4.57:1 (needs 4.5). Raising the dark `accent` further breaks the last one. A new surface or text token joins these sets automatically.

### Using color

- Status is never color alone: a chip has a label and an icon.
- `border` is decoration only. Anything a user must see to use a control (an input's edge, a checkbox) uses `border-strong`.
- Links are `accent-text` and underlined in running text. In nav, menus, and crumbs they rely on position instead of an underline.
- Danger color is for destructive actions and errors only, never for emphasis.
- Disabled and unavailable controls use `bg-muted`, `fg-muted`, and `border`. They are exempt from contrast minimums, but stay readable.

## Typography

System font stacks, no web font (TKN-3). Sizes are rem, so browser zoom and the user's default font size scale everything.

<!-- tokens:fonts:start -->

| Token                    | Value                                                                                                                                   |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| `--font-sans`            | `system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", "Helvetica Neue", Arial, sans-serif, "Apple Color Emoji", "Segoe UI Emoji"` |
| `--font-mono`            | `ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace`                                                |
| `--font-weight-normal`   | `400`                                                                                                                                   |
| `--font-weight-medium`   | `500`                                                                                                                                   |
| `--font-weight-semibold` | `600`                                                                                                                                   |

<!-- tokens:fonts:end -->

<!-- tokens:type:start -->

| Token         | Size        | Line height (`--text-<name>--line-height`) | Use                                                                    |
| ------------- | ----------- | ------------------------------------------ | ---------------------------------------------------------------------- |
| `--text-xs`   | `0.75rem`   | `1rem`                                     | Chips, badges, keyboard hints, table meta (the floor: nothing smaller) |
| `--text-sm`   | `0.8125rem` | `1.125rem`                                 | Compact rows, help text, code and IDs (with `font-mono`)               |
| `--text-base` | `0.875rem`  | `1.25rem`                                  | Body, controls, nav, table cells                                       |
| `--text-md`   | `1rem`      | `1.5rem`                                   | Section headings (h3)                                                  |
| `--text-lg`   | `1.25rem`   | `1.75rem`                                  | Card group headings (h2), sheet and dialog titles                      |
| `--text-xl`   | `1.5rem`    | `2rem`                                     | Page title (h1, one per page)                                          |

<!-- tokens:type:end -->

- Weights: normal for text, medium for labels, nav, and tabs, semibold for headings and buttons. No bold, no italic for emphasis (it reads poorly in many scripts).
- Numbers in tables and counters use `tabular-nums`.
- IDs, keys, endpoints, and code use `font-mono` at `text-sm`.
- Sentence case everywhere: "Create project", not "Create Project".
- Text containers allow 30% growth (UX-34): no fixed widths on labels or buttons, and truncation always keeps the full text as the accessible name plus a tooltip. Turn on the gallery's pseudo locale to see the growth.
- Text the user supplied (names, IDs, keys, endpoints) sits in its own direction isolate, so truncation cuts its end in its own direction (RTL-3, see _Right to left_).

## Spacing and layout

Tailwind's spacing step is 4 px (`p-1` is 4 px). Use these steps: 1, 2, 3, 4, 5, 6, 8, 10, 12, 16 (4 to 64 px). Arbitrary values are a lint error outside `src/ui`.

<!-- tokens:layout:start -->

| Token                     | Value     | Use                                                |
| ------------------------- | --------- | -------------------------------------------------- |
| `--spacing-topbar`        | `3rem`    | Top bar height (48 px)                             |
| `--spacing-sidebar`       | `15rem`   | Primary sidebar width (240 px)                     |
| `--spacing-rail`          | `3.5rem`  | Collapsed icon rail width (56 px)                  |
| `--spacing-resource-list` | `17.5rem` | Resource list default width (280 px; 200 to 480)   |
| `--spacing-list-strip`    | `2.5rem`  | Collapsed resource list strip (40 px)              |
| `--spacing-docs-pane`     | `25rem`   | Docs pane width (400 px)                           |
| `--spacing-dialog-sm`     | `25rem`   | Small dialog width (400 px)                        |
| `--spacing-dialog-md`     | `35rem`   | Medium dialog width (560 px)                       |
| `--spacing-sheet`         | `30rem`   | Side sheet width (480 px)                          |
| `--spacing-palette`       | `40rem`   | Command palette width (640 px)                     |
| `--spacing-toast`         | `22.5rem` | Toast width (360 px)                               |
| `--spacing-auth`          | `25rem`   | Auth card width (400 px)                           |
| `--spacing-control`       | `2rem`    | Control height (32 px)                             |
| `--spacing-control-sm`    | `1.75rem` | Small control height (28 px)                       |
| `--spacing-control-lg`    | `2.5rem`  | Large control height (40 px, auth pages)           |
| `--spacing-content`       | `48rem`   | Maximum width of forms and settings pages (768 px) |

<!-- tokens:layout:end -->

### Density

<!-- tokens:density:start -->

| Variable       | Comfortable | Compact   | Use                                |
| -------------- | ----------- | --------- | ---------------------------------- |
| `--row-height` | `2.5rem`    | `1.75rem` | Grid, table, and list rows (UX-32) |

<!-- tokens:density:end -->

`--row-height` is a plain CSS variable (not a Tailwind token), switched by `data-density="compact"` on `<html>` and used only inside `src/ui`. Forms, the sidebar, and the top bar don't change with density.

### Page anatomy

- Content padding: 24 px at the inline sides from 64rem, 16 px below.
- Page order (UX-36): breadcrumbs in the top bar, then `PageHeader` (title, optional description, primary action at the inline end), then tabs, then content.
- Vertical rhythm: 24 px between page sections, 16 px between cards, 12 px between fields, 8 px between a label and its control group.
- Breakpoint: the shell changes at 64rem (Tailwind `lg`, `useIsWide()` in JavaScript). Other breakpoints (`sm`, `md`) may be used inside content.

## Radius, elevation, and layers

<!-- tokens:shape:start -->

| Token           | Value                                                           | Use                                                              |
| --------------- | --------------------------------------------------------------- | ---------------------------------------------------------------- |
| `--radius-sm`   | `0.25rem`                                                       | Chips, badges, checkboxes, `Kbd`, tooltips                       |
| `--radius-md`   | `0.375rem`                                                      | Buttons, inputs, cards, menus, popovers, toasts                  |
| `--radius-lg`   | `0.5rem`                                                        | Dialogs, sheets (inline start corners only), the command palette |
| `--radius-full` | `9999px`                                                        | Avatars, switch tracks, status dots                              |
| `--shadow-sm`   | `0 1px 2px oklch(0 0 0 / 0.08), 0 2px 8px oklch(0 0 0 / 0.08)`  | Popovers, menus, tooltips, toasts                                |
| `--shadow-lg`   | `0 8px 24px oklch(0 0 0 / 0.16), 0 2px 6px oklch(0 0 0 / 0.08)` | Dialogs, sheets, drawers, the command palette                    |

<!-- tokens:shape:end -->

In the dark theme shadows are faint, so every raised surface also has a `border` edge and uses `bg-raised`.

<!-- tokens:layers:start -->

| Variable      | Value | Use                                                           |
| ------------- | ----- | ------------------------------------------------------------- |
| `--z-sticky`  | `10`  | Sticky table headers, the paging bar                          |
| `--z-drawer`  | `30`  | Sidebar, resource list, and narrow docs drawers               |
| `--z-modal`   | `50`  | Dialogs, sheets, the command palette                          |
| `--z-popover` | `60`  | Menus, popovers, combobox lists (above modals they open from) |
| `--z-toast`   | `70`  | The toast region                                              |
| `--z-tooltip` | `80`  | Tooltips                                                      |

<!-- tokens:layers:end -->

Layers are plain CSS variables, used inside `src/ui` as `z-(--z-modal)` (TKN-1).

## Motion

<!-- tokens:motion:start -->

| Token             | Value                        | Use                                  |
| ----------------- | ---------------------------- | ------------------------------------ |
| `--duration-fast` | `120ms`                      | Hover, press, focus ring, fades      |
| `--duration-base` | `200ms`                      | Popovers, menus, toasts, tooltips    |
| `--duration-slow` | `280ms`                      | Sheets, drawers, the command palette |
| `--ease-standard` | `cubic-bezier(0.2, 0, 0, 1)` | Every transition                     |

<!-- tokens:motion:end -->

- Durations are plain CSS variables (`duration-(--duration-slow)`); `--ease-standard` is a Tailwind token (`ease-standard`).
- Sheets slide from the inline end, drawers from the inline start. Translation is physical, so every slide pairs `translate-x` with its `rtl:` mirror (MOT-1). Popovers fade and scale from 98%.
- Reduced motion (MOT-1): slides and scales are instant; fades of `duration-fast` may stay; the skeleton is static; the running status icon stops spinning.
- Theme switches never animate (THM-5): a transition names only the properties it moves (`transition-[opacity,scale]`, `transition-transform`), never plain `transition`, which would animate colors too.

## Focus

- Every focusable element shows a 2 px solid `focus` outline with a 2 px offset when focused by keyboard (`data-focus-visible`). Inside a scroll container or a dense row, the offset is `-2px` (inset) so the ring isn't clipped.
- The ring is an `outline`, so forced colors mode keeps it.
- Fixed bars and toasts never hide the focused element: the content uses `scroll-padding` for sticky headers, the paging bar, and visible toasts (SHL-8).

## Themes

- System (default), Light, and Dark, chosen in the account menu or the command palette, stored in `orvano:v1:theme`.
- `public/theme-boot.js` sets `data-theme` and `color-scheme` before first paint. Without it, CSS follows the OS.
- `prefers-contrast: more`: `border` becomes `border-strong` and `fg-muted` becomes `fg`.
- Forced colors: focus rings stay; the current nav item, the current tab, and a selected row gain a 2 px inline start mark in `Highlight`, drawn with an inset `box-shadow` or an `outline` so nothing moves; checkboxes, radios, and switches keep the system's rendering; icons use `currentColor` (THM-6).

## Iconography

- Lucide, through `src/ui/icons.ts` only (ICN-1). 16 px in controls and nav, 20 px in empty states and banners, never above 24 px. Stroke 1.75, `currentColor`.
- Decorative by default (`aria-hidden`). An icon only button gets its name from `aria-label` (required by its type).
- Direction icons (chevrons, arrows, back) are flipped in right to left (`flipInRtl`). Logos, media controls, and checkmarks are not.
- Service icons:

| Nav item              | Lucide icon         |
| --------------------- | ------------------- |
| Overview              | `LayoutDashboard`   |
| Auth                  | `Users`             |
| Databases             | `Database`          |
| Storage               | `HardDrive`         |
| Realtime              | `Radio`             |
| Functions             | `SquareFunction`    |
| Sites                 | `Globe`             |
| Messaging             | `Send`              |
| Usage                 | `ChartColumn`       |
| Logs                  | `ScrollText`        |
| Settings              | `Settings`          |
| Projects (org)        | `FolderKanban`      |
| Members (org)         | `UsersRound`        |
| Organization settings | `Building2`         |
| Profile (account)     | `CircleUser`        |
| Security (account)    | `ShieldCheck`       |
| Preferences (account) | `SlidersHorizontal` |

If an icon name changes in a Lucide release, pick the closest replacement and update this table.

## Right to left

- Logical utilities only: `ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`, `text-end`, `rounded-s`, `rounded-e` (and the corner forms like `rounded-ss`), `border-s`, `border-e`, `scroll-ms` (RTL-2). Physical ones fail lint.
- In right to left, the sidebar and drawers sit at the right, sheets and the docs pane open from the left, toasts sit at the bottom left, and the resize separator's arrow keys swap.
- Text the user supplied (names, IDs, keys, endpoints) renders in an isolate (`<bdi>`, or `dir="auto"` on the element that truncates) in the resource list, the breadcrumbs, the switchers, and table cells, so a Latin name in a right to left page keeps its start; the full text stays the accessible name and tooltip (RTL-3).
- Keyboard shortcuts read left to right in every direction: `Kbd` sets `dir="ltr"` with `unicode-bidi: isolate`, so ⌘K never shows as K⌘.
- Check any layout in the gallery with the direction switch, which also gives React Aria the real `ar` locale so its menus and keyboard handling flip (RTL-1). Not the pseudo `ar-XB`: WebKit resolves it as left to right.

## Writing in the UI

- Sentence case for every label, title, and button.
- Buttons say what they do, verb first, with the object when it helps: "Create project", "Delete table", "Copy endpoint". Never "OK", "Yes", or "Submit".
- Errors say what happened and what to do next, in plain words, with no blame: "Couldn't load tables. Check your connection and try again."
- Empty states (UX-20): a short positive title, one sentence, one action.
- Every string is a Lingui message with named placeholders (I18N-1, I18N-4); brand and product names (Orvano, PostgreSQL, GitHub) stay untranslated, so they go in as placeholders (`{product}`, `{host}`), never as words inside the message, and the pseudo locale leaves them alone.
- Error copy by kind (spec 0003 FLT-1), through `useErrorCopy`. Retry shows only where trying again can change the result:

| Kind                 | Says                                                                  | Retry                 |
| -------------------- | --------------------------------------------------------------------- | --------------------- |
| `network`, `timeout` | Couldn't reach the server. Check your connection and try again.       | Yes                   |
| `unauthenticated`    | Your session ended. Sign in again.                                    | No (a Sign in action) |
| `forbidden`          | You don't have access, naming the role you need when the API gives it | No                    |
| `not_found`          | Not found. It may have been deleted, or the link is out of date.      | No                    |
| `conflict`           | It changed or is in use, with what blocks it when the API says        | No                    |
| `validation`         | Something you entered isn't valid, pointing at the field when it can  | No                    |
| `rate_limited`       | Too many requests. Try again in N seconds.                            | Yes                   |
| `server`             | Something went wrong, with the request ID to share                    | Yes                   |

## Shell

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ☰  ◆ Orvano   Org ▾ / Project ▾ / Databases / users     ⟳  ?  ⌘K  (AK)   │  top bar, 48 px, bg-subtle
├────────────┬───────────────┬─────────────────────────────────────────────┤
│ Overview   │ Filter…   [+] │ users                        [Insert row]   │  PageHeader
│ Auth       │ ▸ orders      │ Rows  Columns  Indexes  Permissions  …      │  RouteTabs
│ Databases ●│ ● users       │                                             │
│ Storage    │   payments    │   content                                   │
│ …          │               │                                             │
│ ⟨ Collapse │               │                                             │
└────────────┴───────────────┴─────────────────────────────────────────────┘
 primary sidebar 240 px      resource list 280 px (200 to 480)    main
```

- **Skip link**: the first focusable element, visible only when focused, jumps to `main`.
- **Top bar** (inline start to end): sidebar button (below 64rem), brand mark, breadcrumbs (a `nav` labeled "Breadcrumb") with org and project switchers, then activity indicator, help, command palette (shows Mod+K), account menu (the avatar). Same order in every scope and at every width (SHL-4): nothing moves into the account menu on small screens.
- **Breadcrumbs**: org and project crumbs show their names and open the switchers; page crumbs are links except the last (`aria-current="page"`, also when the last crumb is the org or project). Below 64rem every crumb except the first and last collapses into a menu; a collapsed page crumb with no link is plain text in that menu, never a dead item. Below 25rem (400 px) only the collapsed menu and the current page show; the org and project items in the menu open their switchers. Every crumb control is at least 24 by 24 px, and the current page truncates with its full name as accessible name and tooltip.
- **Switchers**: open with recent or first results at once; the current org or project has a check icon and `aria-current="true"`, and the highlight starts on it; search 150 ms after typing; up to 20 results with the count announced; error shows a message and Retry under the list and is announced once (then the retry's result) through the announcer, not a new live region (AC-12); "All projects" and "New project" links at the bottom.
- **Primary sidebar**: the current scope's nav (project services, org pages, or account pages); current item on `accent-subtle`; collapse button at the bottom; 56 px icon rail when collapsed, names in tooltips. Below 64rem it's a drawer, which closes itself when the window widens.
- **Drawers** (sidebar, resource list): a visible close `IconButton` at the drawer's inline end top, as the docs pane has, besides Escape and the scrim; closing returns focus to the opener.
- **Resource list** (services with collections): filter box and create button on top, the list below, a resize separator at its inline end (arrow keys move it 16 px, Home and End jump to the limits, and it never squeezes the content below 480 px), and a collapse button. The separator draws a 4 px line but its hit area is 24 px wide. After the filter settles, the announcer says how many items match. Collapsed, it's a 40 px strip with an expand button. Below 64rem it's a drawer opened by "Show list".
- **Account menu**: avatar, name, and email; Theme (System, Light, Dark); Density (Comfortable, Compact); Keyboard shortcuts; Sign out.
- **Lazy panels** (palette, shortcut help, docs pane; PRM-10): their code preloads when the browser is idle and when the pointer or focus reaches their button; if the panel still isn't ready after 300 ms, its button shows a busy state (`useDelayedPending`).
- **Docs pane**: a "Help" region at the inline end. From 64rem it takes 400 px and the content narrows beside it (nothing is covered); below 64rem it's a full width drawer. Focus moves to its heading; Escape and Close return focus to the help button.
- **Route tabs**: never wrap and have no More menu. When they don't fit, the row scrolls on the inline axis, the current tab is scrolled into view, and a fade marks each cut edge (SHL-14).
- **Pages without the shell** (sign in, sign up, first run setup) use `AuthLayout`: the brand mark over a centered 400 px card on `bg-subtle`, large (40 px) controls and fields.

## Components

Every component is in `src/ui`, built on React Aria Components where one exists (PRM-2), and takes no `className` or `style` (PRM-3). Each is shown in the gallery in every variant and state. "Keys" lists the keyboard model beyond Tab; React Aria implements the APG pattern named. Components marked _lazy_ are imported by path and loaded on first use (PRM-10).

### Button recipes

| Variant     | Rest                                                | Hover and pressed | Focus      | Unavailable or disabled                         |
| ----------- | --------------------------------------------------- | ----------------- | ---------- | ----------------------------------------------- |
| `primary`   | `bg-accent`, `text-accent-fg`, `border-transparent` | `bg-accent-hover` | focus ring | `bg-bg-muted`, `text-fg-muted`, `border-border` |
| `secondary` | `bg-bg`, `text-fg`, `border-border-strong`          | `bg-bg-muted`     | focus ring | `bg-bg-muted`, `text-fg-muted`, `border-border` |
| `ghost`     | transparent, `text-fg`                              | `bg-bg-muted`     | focus ring | `text-fg-muted`                                 |
| `danger`    | `bg-danger`, `text-danger-fg`, `border-transparent` | `bg-danger-hover` | focus ring | `bg-bg-muted`, `text-fg-muted`, `border-border` |

Sizes: `sm` 28 px, `md` 32 px, `lg` 40 px high, `text-base` (`text-sm` for `sm`), `radius-md`, 12 px inline padding (8 px for `sm`). Pressed adds no movement. The transparent border on `primary` and `danger` is what forced colors mode paints, so they keep a boundary there. Selected rows and options use `bg-accent-subtle` with `text-accent-subtle-fg`.

### Foundations

| Component        | Built on                    | Variants and states                                                | Keys          | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ---------------- | --------------------------- | ------------------------------------------------------------------ | ------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Button`         | `Button`                    | the recipes above; pending (spinner, ignores presses), unavailable | Enter, Space  | One `primary` per surface, at the inline end (UX-36). `unavailableReason` keeps it focusable and explains why in a tooltip and `aria-describedby` (PRM-4)                                                                                                                                                                                                                                                                                                                                              |
| `IconButton`     | `Button`                    | `ghost`, `secondary`; `sm`, `md`                                   | Enter, Space  | `aria-label` required; tooltip shows the same label                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `Link`           | `Link` + `createLink`       | inline (underlined), nav (no underline)                            | Enter         | Router aware, preloads on hover and focus (RTE-6)                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `Icon`           | Lucide                      | 16, 20, 24 px                                                      |               | `aria-hidden` unless `label`; `flipInRtl`                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `Avatar`         | semantic HTML               | `sm` 24 px, `md` 32 px                                             |               | Up to two initials on `accent-subtle` (PRM-11), each the first grapheme (`Intl.Segmenter`), `aria-hidden`; drawn from `data-initials` by CSS so the "Account" button's visible text matches its name                                                                                                                                                                                                                                                                                                   |
| `Tooltip`        | `TooltipTrigger`, `Tooltip` |                                                                    | Escape closes | `tooltip` on `tooltip-fg`, `radius-sm`, `text-xs`; opens on hover and focus, stays while hovered, never holds essential info (UX-40)                                                                                                                                                                                                                                                                                                                                                                   |
| `Kbd`            | `kbd`                       |                                                                    |               | One `kbd` per key inside an outer `kbd`, `radius-sm`, `bg-muted`, `text-xs`. Mod shows as ⌘ on Apple platforms, Ctrl (translated) elsewhere (PRM-11). The outer `kbd` is `dir="ltr"` with `unicode-bidi: isolate`, so keys keep their order in right to left (RTL-3)                                                                                                                                                                                                                                   |
| `ExternalLink`   | `Link`                      | inline                                                             | Enter         | Opens in a new tab with `rel="noopener noreferrer"`, an external icon, and "(opens in a new tab)" for screen readers. Renders a link only for an `http:` or `https:` address, plain text otherwise. Standalone (not inside a sentence), its target is at least 24 px high. Only the docs fallback uses it (SHL-9)                                                                                                                                                                                      |
| `useShortcut`    | hook                        |                                                                    |               | The only way to bind a shortcut (A11Y-7): adds it to the registry `ShortcutHelp` lists and returns `aria-keyshortcuts` for its visible control. Nothing fires while a modal is open (each modal overlay carries `data-modal`), a one character shortcut never fires while you type in a field, and a held key (`event.repeat`) fires once. Modifier shortcuts match letters on the physical key (`event.code`, so Mod+K works on any keyboard layout) and symbols on the typed character (`event.key`) |
| `VisuallyHidden` | `VisuallyHidden`            |                                                                    |               | Text for screen readers only                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |

### Shell

| Component                        | Built on                                                      | Variants and states                             | Keys                                                | Notes                                                                                                                                                                                                                                                                                                             |
| -------------------------------- | ------------------------------------------------------------- | ----------------------------------------------- | --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AppShell`                       | semantic HTML                                                 | scope `project`, `org`, `account`; wide, narrow |                                                     | Landmarks per SHL-3; takes `ShellModel` and `crumbs`                                                                                                                                                                                                                                                              |
| `SkipLink`                       | `a` to `#main`                                                | hidden, visible on focus                        | Tab, Enter                                          | The first focusable element (UX-1, SHL-3); moves focus to `main` itself, so the router sees no hash change                                                                                                                                                                                                        |
| `PrimarySidebar`                 | `nav` + `Link`                                                | expanded, rail, drawer                          | Tab through items                                   | `aria-current="page"` on the current item; Overview matches exactly                                                                                                                                                                                                                                               |
| `Drawer`                         | `ModalOverlay`, `Modal`, `Dialog`                             | from inline start or end                        | Escape closes                                       | Labeled, a visible close `IconButton` at the inline end, traps focus, returns it to the opener, closes when wide                                                                                                                                                                                                  |
| `Breadcrumbs`                    | `Breadcrumbs` in a `nav`                                      | full, collapsed (below 64rem)                   |                                                     | Only the last crumb is `aria-current="page"` (the others match exactly); collapsed crumbs in a `Menu`; below 25rem only that menu and the current page (SHL-4); every control at least 24 by 24 px                                                                                                                |
| `useDocumentTitle`               | a hook over `document.title`                                  |                                                 |                                                     | Builds `Resource · Service · Project · Orvano` from the same crumbs, the org left out (UX-4)                                                                                                                                                                                                                      |
| `OrgSwitcher`, `ProjectSwitcher` | `DialogTrigger`, `Popover`, `Autocomplete`, `ListBox`         | loading, results, empty, error with Retry       | Arrows, Enter, Escape                               | SHL-12: the current item marked (check icon, `aria-current="true"`) and highlighted on open; names in an isolate (RTL-3)                                                                                                                                                                                          |
| `ResourceLayout`                 | semantic HTML                                                 | list open, collapsed strip, drawer              |                                                     | Keeps the list mounted (UX-2); passed as `AppShell`'s `resourceList`, so it sits before `main`                                                                                                                                                                                                                    |
| `ResourceList`                   | `nav` + `Link`, `SearchField`                                 | loading, empty, filtered empty                  |                                                     | `preload={false}` links (RTE-6); names in an isolate (RTL-3), truncated with tooltips; the filter is locale aware and its match count is announced once it settles; loading shows `ContentSkeleton` through `useDelayedPending`                                                                                   |
| Resize separator                 | `useMove`, `role="separator"`                                 |                                                 | Arrows ±16 px (swapped in right to left), Home, End | SHL-13; a 4 px visible line with a 24 px wide hit area                                                                                                                                                                                                                                                            |
| `PageHeader`                     | semantic HTML                                                 | with or without description and action          |                                                     | Renders the page's `h1` (`tabIndex={-1}`, RTE-7)                                                                                                                                                                                                                                                                  |
| `RouteTabs`                      | `nav` + `Link`                                                |                                                 | Tab                                                 | Links with `aria-current`, not ARIA tabs (UX-5); on overflow the row scrolls inline with the current tab in view and a fade at a cut edge (SHL-14)                                                                                                                                                                |
| `Tabs`                           | `Tabs`                                                        |                                                 | Arrows, Home, End                                   | Manual activation when panels load (UX-5)                                                                                                                                                                                                                                                                         |
| `AccountMenu`                    | `MenuTrigger`, `Menu`, `MenuSection`                          |                                                 | Arrows, Enter, Escape                               | Theme and Density are single selection sections                                                                                                                                                                                                                                                                   |
| `ActivityIndicator`              | `DialogTrigger`, `Popover`                                    | idle, has messages, (later) running jobs        | Escape                                              | "Running jobs" ("No running jobs" until feature 46) and the message history, last 50, newest first (UX-24). A dot and the name "Activity, new messages" mark messages that arrived while it was closed (messages seen while it is open don't count)                                                               |
| `HelpButton`                     | `IconButton`                                                  | pane closed, pane open (`aria-expanded`)        | Enter, Space                                        | Same place in every scope (UX-31); focus returns here when the pane closes                                                                                                                                                                                                                                        |
| `DocsPane` (_lazy_)              | `aside`, `Drawer` below 64rem                                 | open beside content, drawer, fallback           | Escape closes                                       | SHL-9: 400 px beside `main`, which narrows; a full width drawer from the inline end below 64rem; focus starts on its heading; until feature 32 a message and the docs fallback `ExternalLink`                                                                                                                     |
| `PaletteButton`                  | `Button`                                                      | wide (label and keys), narrow (icon)            | Enter, Space                                        | The visible control for Mod+K, with `aria-keyshortcuts`; its name comes from its content ("Commands ⌘K", "Commands")                                                                                                                                                                                              |
| `CommandPalette` (_lazy_)        | `ModalOverlay`, `Dialog`, `Autocomplete`, `TextField`, `Menu` | empty query (recents), results, no results      | Mod+K, arrows, Enter, Escape                        | Local actions from `palette-actions.ts` now, server results later (SHL-10). The input is a `combobox`; Escape closes at once. Recents (10) come first for an empty query, and the Go to group then leaves them out, so no action shows twice. A chosen action runs after the palette has closed and focus is back |
| `ShortcutHelp` (_lazy_)          | `Dialog`                                                      |                                                 | `?`, Escape                                         | Lists the `useShortcut` registry with each shortcut's keys                                                                                                                                                                                                                                                        |
| `AuthLayout`                     | semantic HTML                                                 |                                                 |                                                     | Centered card for pages without the shell                                                                                                                                                                                                                                                                         |
| `BrandMark`                      | inline SVG                                                    | full, mark only                                 |                                                     | `aria-hidden`; its link is "Orvano home"                                                                                                                                                                                                                                                                          |

### Forms

All fields render through `useAppForm` (spec 0003 FRM-1). Label above, help text under the label, "(optional)" on optional fields, error under the control (UX-7, UX-8). Inputs are `bg`, `border-strong`, `radius-md`, 32 px high; invalid adds a `danger-text` border and message. Screens pass `useAppForm` a schema and `onSubmitAsync`, never validator timing (FRM-2). When a pointer press moves focus out of a field, its error appears once the press ends or is cancelled (a touch scroll), so the shift can't pull a button out from under the pointer. `isDisabled` and `isReadOnly` work on every field.

| Component                                | Built on                         | Variants and states                          | Keys                  | Notes                                                                                                                                                                                                                                                 |
| ---------------------------------------- | -------------------------------- | -------------------------------------------- | --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TextField`, `PasswordField`, `TextArea` | `TextField`, `Input`, `TextArea` | default, focus, invalid, read only, disabled |                       | `PasswordField` has a show and hide toggle inside the field, at its inline end                                                                                                                                                                        |
| `NumberField`                            | `NumberField`                    |                                              | Arrows step           | Locale formatted                                                                                                                                                                                                                                      |
| `Select`                                 | `Select`, `Popover`, `ListBox`   |                                              | Arrows, type ahead    | The trigger carries `aria-invalid` when the field is invalid                                                                                                                                                                                          |
| `Combobox`                               | `ComboBox`                       | loading, empty                               | Arrows, Enter, Escape |                                                                                                                                                                                                                                                       |
| `Checkbox`, `CheckboxGroup`              | `Checkbox`, `CheckboxGroup`      | checked, indeterminate, invalid              | Space                 | Box 16 px inside a 24 px target                                                                                                                                                                                                                       |
| `RadioGroup`                             | `RadioGroup`, `Radio`            |                                              | Arrows                |                                                                                                                                                                                                                                                       |
| `Switch`                                 | `Switch`                         | on, off, unavailable, disabled               | Space                 | Immediate effect settings only; forms use `Checkbox`. A setting you can't change uses `unavailableReason` (PRM-4): it stays focusable, ignores Space, and shows the reason as its description; `isDisabled` only for a switch that can't apply at all |
| `SubmitButton`                           | `Button`                         | pending                                      |                       | Never disabled for invalid input; ignores a second press (FRM-4)                                                                                                                                                                                      |
| `ErrorSummary`                           | `role="group"` with links        |                                              |                       | Gets focus after a failed submit; links focus each field                                                                                                                                                                                              |
| `UnsavedChangesGuard`                    | `useBlocker` + `Dialog`          |                                              |                       | One question for any number of dirty forms (RTE-8)                                                                                                                                                                                                    |

### Overlays

| Component                | Built on                          | Variants and states                                                                                  | Keys                              | Notes                                                                                                                                                                                                                                    |
| ------------------------ | --------------------------------- | ---------------------------------------------------------------------------------------------------- | --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Dialog`                 | `ModalOverlay`, `Modal`, `Dialog` | `sm` (400 px), `md` (560 px)                                                                         | Escape                            | Footer: Cancel then primary, inline end (UX-36). A `description` is linked with `aria-describedby`; alert dialogs (`ConfirmDelete`, `UnsavedChangesGuard`) always set it                                                                 |
| `SideSheet` (_lazy_)     | `ModalOverlay`, `Modal`, `Dialog` | modal; 480 px, full width below 640 px                                                               | Escape (asks if dirty)            | Opens from the inline end; the non modal row sheet comes with the rows feature                                                                                                                                                           |
| `Popover`                | `Popover`                         |                                                                                                      | Escape                            | `shadow-sm`, `bg-raised`, `border`                                                                                                                                                                                                       |
| `Menu`                   | `MenuTrigger`, `Menu`             | items, sections, danger item, unavailable item                                                       | Arrows, Enter, Escape, type ahead | An unavailable item shows its reason as a description line and never fires, through its own `onAction` or the menu's (PRM-4)                                                                                                             |
| `ConfirmDelete` (_lazy_) | `Dialog`                          | tier 1 (type the name), tier 2, single row, bulk; dependents loading, counted, about, couldn't count |                                   | Tier from `tiers.ts` (UX-23, PRM-6); focus starts on Cancel for tier 2. A typed name or count must match the whole trimmed value, with locale digits read as ASCII; a failure shows through `InlineError` in the words of its error kind |

### Feedback and states

| Component                                     | Built on                             | Variants and states                               | Keys                                    | Notes                                                                                                                                                                                                                                |
| --------------------------------------------- | ------------------------------------ | ------------------------------------------------- | --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `notify`                                      | function                             | `success`, `error`, `info`                        |                                         | The only way to raise a message: history, merge, queue (PRM-8)                                                                                                                                                                       |
| `Announcer`                                   | `@react-aria/live-announcer`         | polite, assertive                                 |                                         | Shared with React Aria's own announcements (A11Y-4)                                                                                                                                                                                  |
| `Toast`                                       | React Aria toast region              | `success`, `error`, `info`; count when merged     | F6 reaches the region, Escape dismisses | Bottom inline end, three max, `bg-raised`, `shadow-sm`, a 3 px inline start edge in the tone's `-text` color. An identical toast merges with a count, restarts its timer, and is announced again                                     |
| `Banner`                                      | `role="status"` (`alert` for danger) | `neutral`, `info`, `warning`, `danger`, `success` |                                         | `AppShell`'s `banner` slot, under the top bar, one at a time (the caller picks which state wins). `-subtle` surface (`bg-muted` for neutral) with a 3 px inline start edge in the tone's `-text` color (`border-strong` for neutral) |
| `InlineError`                                 | `role="alert"`                       |                                                   |                                         | Next to its source, never closes by itself. One of the four allowed live regions (AC-12)                                                                                                                                             |
| `EmptyState`                                  | semantic HTML                        | first use, no results, not allowed or not set up  |                                         | Illustration `aria-hidden` (UX-20)                                                                                                                                                                                                   |
| `ErrorState`                                  | semantic HTML                        | page error, not found, forbidden                  |                                         | What failed, Retry only for kinds a retry can fix, copyable request ID (UX-22; copy by kind under _Writing in the UI_). When it replaces content that was on screen, it says so once through the announcer                           |
| `Skeleton`, `GridSkeleton`, `ContentSkeleton` | semantic HTML, `aria-busy`           |                                                   |                                         | `bg-muted` blocks; shimmer off under reduced motion; `GridSkeleton` draws 8 rows. `aria-busy` goes on the region being loaded, and the skeleton keeps its hidden "Loading" text                                                      |
| `useDelayedPending`                           | hook                                 |                                                   |                                         | 300 ms delay, 500 ms minimum (UX-21)                                                                                                                                                                                                 |
| `Progress`                                    | `ProgressBar`                        | determinate, indeterminate                        |                                         | With percentage when known                                                                                                                                                                                                           |
| `Spinner`                                     | `ProgressBar` (indeterminate)        |                                                   |                                         | Inside buttons and small controls only                                                                                                                                                                                               |
| `StatusChip`                                  | semantic HTML                        | `status`: see below                               |                                         | Always label plus icon                                                                                                                                                                                                               |
| `Badge`                                       | semantic HTML                        | `neutral`, `info`, `success`, `warning`, `danger` |                                         | Counts and short labels, `-subtle` surface, `-text` color                                                                                                                                                                            |

`StatusChip` statuses (UX-29):

| Status      | Tone                             | Lucide icon                                         |
| ----------- | -------------------------------- | --------------------------------------------------- |
| `queued`    | neutral (`bg-muted`, `fg-muted`) | `Clock`                                             |
| `running`   | info                             | `LoaderCircle` (spins, static under reduced motion) |
| `ready`     | success                          | `CircleCheck`                                       |
| `failed`    | danger                           | `CircleX`                                           |
| `cancelled` | neutral (`bg-muted`, `fg-muted`) | `CircleSlash`                                       |

### Data display

| Component             | Built on                               | Variants and states                                          | Keys                              | Notes                                                                                                                                                                                                                     |
| --------------------- | -------------------------------------- | ------------------------------------------------------------ | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Table` (_lazy_)      | `Table`                                | density aware rows, sortable headers, selectable rows, empty | Arrows between rows and cells     | Static lists (API keys, members); grids use `DataGrid` later. The scroll container is a named region with scroll padding for the sticky header and the scrollbar, so neither covers a focused row; masked keys don't wrap |
| `Pagination` (_lazy_) | `Button` + `Menu`                      | first and last page unavailable (focusable, PRM-4)           | Tab; arrows in the page size menu | Below a list (UX-14); from `@/ui/table`. The caller holds the cursors (spec 0014 PAG-2). A total comes when a list returns one                                                                                            |
| `SettingsCard`        | semantic HTML                          |                                                              |                                   | Title, description, fields, Update button enabled only when changed (FRM-5). The caller enforces it from the form's dirty state (through `footer`); the card doesn't know the form                                        |
| `DangerZone`          | semantic HTML                          |                                                              |                                   | `danger-subtle` edge; destructive buttons apart from primary actions                                                                                                                                                      |
| `CopyField`           | `TextField` (read only) + `IconButton` | copied, copy unavailable                                     |                                   | Announces "Copied"; without the clipboard, selects the value and shows the keyboard hint (UX-33, PRM-9)                                                                                                                   |
| `SecretReveal`        | `CopyField` + warning `Banner`         | shown once, then masked                                      |                                   | Never stores the secret (PRM-7); a new `secret` prop starts it over (shown again, not masked)                                                                                                                             |
| `CodeSnippet`         | `pre` + `CopyField` button             |                                                              |                                   | Plain mono on `bg-muted`, no highlighting yet. The focusable `pre` is a named region described by its copy hint; without the clipboard the hint is announced                                                              |
| `RelativeTime`        | `time` + `Tooltip`                     |                                                              | Focusable for the tooltip         | Relative up to 7 days, then a medium date; exact local and UTC time on hover and focus (UX-34, PRM-11). An invalid date shows the raw value. A `focusable` prop, off in tables and lists so rows aren't full of tab stops |

### Later, by the feature that needs them

`DataGrid` and its parts on react-data-grid (rows feature), the non modal row `SideSheet` (rows feature), `LogStream` (feature 46), `JsonEditor` and `RelationPicker` (features 24 and 16), chart primitives and chart color tokens (feature 45). Each follows this file and adds its section here.
