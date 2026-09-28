# Console design system

This is the visual source of truth for the Orvano console (spec 0005). Every value here is implemented in [`src/styles/tokens.css`](src/styles/tokens.css). Components use only those tokens, and a test fails if a color literal (`#`, `rgb(`, `hsl(`, `oklch(`) appears anywhere in `console/src` outside `tokens.css`. If you change a value, change it in both places.

The look is dark first, with an indigo violet accent (OKLCH hue 280), Inter for the interface, and JetBrains Mono for code. Light and dark are both complete, and there are two densities.

## How it is built

- Tailwind CSS v4. Tokens are CSS custom properties on `:root` (dark, the default), `:root[data-theme='light']`, and `:root[data-density='comfortable']`. An `@theme inline` block maps them to utilities such as `bg-background`, `text-muted-foreground`, and `h-(--control-h)`.
- Components are copied from the shadcn registry in its Base UI flavor (`pnpm dlx shadcn@latest add <name>`, never a dependency) into `src/components/ui/` and then restyled to the tokens. Base UI supplies focus, keyboard, and overlay behavior. Use `render`, not `asChild`.
- Token names follow shadcn's, so copied components work unchanged. Where shadcn's defaults disagree with this file (the translucent `ring-ring/50` focus style, the sidebar's cookie and `Ctrl+B`), this file wins.
- `theme-init.js` runs before the first paint and sets `data-theme`, `data-density`, and `color-scheme` on `<html>` from `localStorage`. `PreferencesProvider` owns both values afterwards.
- `cn()` in `src/lib/utils.ts` knows the type role classes (`text-body`, `text-small`, `text-mono`, `text-h3`) are font sizes. Do not replace it with a plain `twMerge`.

## Color

Values are `oklch(lightness chroma hue)`. The contrast test (`src/styles/contrast.unit.test.ts`, pairs in `contrast-pairs.ts`) is the judge: if a pair fails, change only the lightness until it passes and update this table.

| Token | Dark (default) | Light | Use |
|---|---|---|---|
| `--background` | `0.155 0.006 280` | `0.985 0.002 280` | Page |
| `--foreground` | `0.965 0.004 280` | `0.21 0.01 280` | Body text |
| `--card` | `0.185 0.007 280` | `1 0 0` | Cards, panels |
| `--popover` | `0.205 0.008 280` | `1 0 0` | Menus, popovers, dialogs |
| `--muted` | `0.235 0.008 280` | `0.962 0.004 280` | Quiet fills, skeletons |
| `--muted-foreground` | `0.72 0.012 280` | `0.48 0.015 280` | Secondary text, hints |
| `--secondary` | `0.255 0.009 280` | `0.945 0.005 280` | Secondary buttons |
| `--accent` | `0.27 0.012 280` | `0.94 0.012 280` | Hover and selected surfaces (shadcn's meaning of accent) |
| `--primary` | `0.54 0.21 280` | `0.51 0.23 280` | Primary buttons, brand |
| `--primary-foreground` | `0.99 0 0` | `0.99 0 0` | Text on primary |
| `--link` | `0.76 0.12 280` | `0.48 0.2 280` | Text links |
| `--ring` | `0.72 0.15 280` | `0.51 0.23 280` | Focus ring |
| `--border` | `0.28 0.008 280` | `0.905 0.005 280` | Decorative dividers only |
| `--input` | `0.52 0.012 280` | `0.62 0.012 280` | Control borders (3:1 against surfaces) |
| `--destructive` | `0.54 0.2 25` | `0.54 0.21 27` | Destructive buttons, error badges |
| `--danger-text` | `0.72 0.17 22` | `0.5 0.19 27` | Error text and icons on page surfaces |
| `--success` | `0.52 0.13 155` | `0.5 0.13 155` | Success badges |
| `--success-text` | `0.78 0.15 155` | `0.45 0.12 155` | Success text and icons |
| `--warning` | `0.72 0.15 75` | `0.8 0.15 80` | Warning badges (dark text on amber) |
| `--warning-foreground` | `0.2 0.02 75` | `0.25 0.04 70` | Text on warning |
| `--warning-text` | `0.82 0.14 80` | `0.48 0.11 70` | Warning text and icons |
| `--sidebar` | `0.135 0.006 280` | `0.97 0.003 280` | Sidebar surface |
| `--sidebar-accent` | `0.24 0.02 280` | `0.93 0.02 280` | Current sidebar entry |

Also defined: `--card-foreground`, `--popover-foreground`, `--secondary-foreground`, `--accent-foreground`, and `--sidebar-foreground` equal `--foreground`. `--destructive-foreground` and `--success-foreground` are `0.99 0 0`. `--sidebar-primary`, `--sidebar-border`, and `--sidebar-ring` follow `--primary`, `--border`, and `--ring`.

Status never relies on color alone. Every badge carries its word (Active, Setting up, Failed, Deleting), and error text carries an icon.

Contrast rules: text pairs meet 4.5:1, control borders and focus rings meet 3:1 against the surface they sit on, in both themes. `--border` is decoration only and is never the only thing that marks a control.

## Type

Inter for the interface (`font-feature-settings: 'cv11'`, one story `a`), JetBrains Mono for IDs, keys, and code. Both are bundled, so the console never loads a font from another host. Table numbers use `tabular-nums`. The type roles change with density through the `--fs-*` and `--lh-*` tokens, which `@theme inline` exposes as `text-body`, `text-small`, `text-mono`, and `text-h3`. Copied shadcn components use `text-sm` and `text-xs`, which are mapped to the body and small roles.

| Role | Compact | Comfortable | Weight |
|---|---|---|---|
| Page title (`h1`) | 24/32 px | 24/32 px | 600 |
| Section title (`h2`) | 18/28 px | 18/28 px | 600 |
| Subsection (`h3`, `text-h3`) | 15/22 px | 16/24 px | 600 |
| Body (`text-body`, `text-sm`) | 14/20 px | 16/24 px | 400 |
| Label, button | 14/20 px | 16/24 px | 500 |
| Small, hint (`text-small`, `text-xs`) | 12/16 px | 14/20 px | 400 |
| Mono (`text-mono`) | 13/20 px | 14/20 px | 400 |

## Density

`data-density` on `<html>`. The default is compact. Every component reads these tokens, and a test measures them.

| Token | Compact | Comfortable | Used by |
|---|---|---|---|
| `--control-h` | 2rem (32 px) | 2.5rem (40 px) | Buttons, inputs, selects |
| `--control-h-sm` | 1.75rem (28 px) | 2rem (32 px) | Small buttons, menu items (every target stays at 24 px or more, WCAG 2.5.8) |
| `--control-px` | 0.625rem | 0.875rem | Horizontal padding in controls |
| `--row-h` | 2.25rem (36 px) | 2.75rem (44 px) | Table rows, switcher items |
| `--icon` | 1rem | 1.125rem | Lucide icon size |
| `--stack` | 0.75rem | 1rem | Default gap between form fields |

## Spacing and layout

Tailwind's 4 px scale for everything else. Top bar `--topbar-h` 48 px. Sidebar 240 px expanded (`--sidebar-w`), 56 px as a rail (`--sidebar-w-rail`), drawer 280 px (`--drawer-w`). Page padding `--page-px` is 24 px from 640 px up and 16 px below. Forms are at most 640 px wide, long text at most 65 characters a line, tables take the full width and scroll inside their own focusable, labelled container.

## Radius, elevation, motion

- Radius: `--radius` is 0.5rem, so `rounded-sm` is 4 px (badges), `rounded-md` 6 px (controls), `rounded-lg` 8 px (cards, dialogs, popovers), `rounded-xl` 12 px (empty state illustrations).
- Elevation: surfaces step up in lightness (background, card, popover) with a 1 px `--border`. Shadows only on overlays, through `--shadow-overlay`.
- Motion: overlays only. Enter 150 ms (drawer 200 ms), exit 100 ms, easing `--ease` (`cubic-bezier(0.2, 0, 0, 1)`), through `tw-animate-css`. With `prefers-reduced-motion: reduce` every duration is 0. Page changes never animate.

## Focus

`:focus-visible { outline: 2px solid var(--ring); outline-offset: 2px }` on every interactive element. Inside inputs the ring sits on the border (offset 0). Keyboard focus shows the ring, a mouse click does not. Never remove an outline without a replacement. Dialogs, drawers, menus, and popovers trap focus while open, close on Escape, and return focus to the element that opened them.

## Breakpoints and layers

Tailwind defaults: `sm` 640, `md` 768, `lg` 1024, `xl` 1280. Below `lg` the sidebar is a drawer, and below `sm` the top bar keeps only the deepest switcher (the org switcher moves into the drawer). Layers: sticky headers `--z-sticky` 10, sidebar 20, overlays 50, toasts `--z-toast` 100.

## Icons and logo

Lucide only, at `--icon` size, stroke 2. Icons are `aria-hidden` unless one is the only content of a control, in which case the control has an `aria-label` (and a tooltip when the meaning is not obvious). The logo is a text wordmark "Orvano" in Inter 600 beside a ring mark in `--primary` (`src/shell/logo.tsx`, `public/favicon.svg`). Swapping in a real logo changes those files, not code.

## UI text

- Sentence case everywhere. Buttons are verbs ("Delete project", never "OK"). No trailing period on labels or buttons.
- IDs, keys, and code are in mono with a copy button.
- Dates go through `Intl.DateTimeFormat` in the browser's locale and time zone. Recent times read as relative time ("3 minutes ago") with the full date in a tooltip.
- English only. Strings live in the components.
- Errors show the message, the code, and the request ID, never raw JSON or a stack trace.

## Components

All live in `src/components/ui/`. Every component and variant has a live example in `src/dev/examples.tsx` (shown at `/dev/components` in development) and an entry in `keyboard-scripts.ts`. The shared browser test presses each script's keys, checks the result, and runs axe in dark and light with zero violations allowed.

| Component | Variants and states | Keyboard |
|---|---|---|
| Button | primary, secondary, outline, ghost, destructive, link; sizes default, sm, icon; loading (spinner, `aria-busy`, still focusable), disabled, and `disabledReason` (`aria-disabled`, focusable, does nothing, reason in a tooltip and as its description) | Enter and Space activate |
| Input, Textarea | default, invalid (`aria-invalid`), disabled, read only | native |
| Select | default, invalid, disabled | arrows, typeahead, Enter, Escape |
| Checkbox, Switch | checked, unchecked, indeterminate (checkbox), disabled | Space toggles |
| Field | label, hint, error linked with `aria-describedby`, required marker | label click focuses the control |
| Form alert | error, warning, info, success; `role="alert"` for errors | none |
| Dialog | default, with form | focus trap, Escape closes, focus returns |
| Confirm dialog | default, destructive, optional typed name confirmation | focus starts on Cancel, Escape cancels |
| Dropdown menu | items, checkbox and radio items, separators, submenus, destructive item | arrows, typeahead, Enter, Escape, Right and Left for submenus |
| Popover | default | Escape closes, focus returns |
| Combobox | search, grouped, loading more, empty, footer slot | arrows, typing filters, Enter picks, Escape closes |
| Tooltip | default | shows on focus and hover, Escape hides (WCAG 1.4.13) |
| Tabs | default, automatic activation | arrows move and activate |
| Toast | success (5 s, pauses on hover and focus), error (stays), with action; at most 3 visible | F6 moves to the toast region, Escape dismisses |
| Table, DataTable | loading (skeleton rows), empty, error with Retry, "Load more" for cursor paging, optional sort over loaded rows, sticky header | the scroll container is focusable and labelled; sort buttons in headers |
| Badge | neutral, primary, success, warning, destructive, status (dot plus word) | not interactive |
| Card | default, interactive (the whole card is one link) | link focus |
| Empty state | icon, title, text, action slot | none |
| Skeleton, Spinner | text, block, circle | `aria-hidden`; the loading region has `aria-busy` |
| Breadcrumb | with switchers, collapsed | links |
| Sidebar | expanded, rail, drawer; state in `localStorage` (`orvano.sidebar`), never a cookie | `[` toggles (ignored while typing); the drawer traps focus |
| Separator, Kbd | none | none |
| Copy button | idle, copied (2 s, announced "Copied" through a polite live region), failed | Enter and Space |
| Code block | inline, block with copy button, no syntax colors yet | the scroll container is focusable |

## Patterns

- **Forms**: TanStack Form with a Zod schema, `Field` for label, hint, and error. A field error shows under the field. A server error on submit shows in a `FormAlert` at the top of the form (the server sends no per field list yet).
- **Toasts**: only through `notifySuccess` and `notifyError` in `src/lib/toast.ts`. A failed action shows an error toast that stays until dismissed.
- **Errors**: a page whose data fails to load shows `ErrorPanel` with the message, code, request ID, and a Retry button.
- **Roles**: `useOrgRole()` reads your role in the org in context. `RoleGate` hides owner only areas. An action your role cannot take uses `<Button disabledReason="Owners only">`. The API's 403 stays the real guard.
- **Page headings**: every page renders its `h1` through `PageHeading` (`id="page-title"`), and sets its title with `usePageTitle`. After an in app navigation the root moves focus to the heading.
- **Sidebar entries**: declared only in `src/shell/nav.ts`. Product rows add a line there, never their own navigation.

## Security exception log

The Content Security Policy allows scripts and styles only from the console's own files. Fonts are not inlined as `data:` URIs (`assetsInlineLimit` in `vite.config.ts`), because `font-src` is `'self'`. Record every exception here, with the exact text it allows.

| Allowed by hash | Why | Where it lives |
|---|---|---|
| `'sha256-kLmvWqfziFavKtqHqRsb90f006UAK2Dmd0It5Iz2KFA='` in `style-src` | Base UI's Select popup (and ScrollArea) injects one `<style>` that hides the popup's scrollbar: `.base-ui-disable-scrollbar{scrollbar-width:none}.base-ui-disable-scrollbar::-webkit-scrollbar{display:none}`. Found by spec 0007's Expiry and Type selects. | `deploy/gateway/Caddyfile`; `e2e/headers.spec.ts` opens both selects and fails on any violation |

A Base UI upgrade that changes that text changes the hash: the end to end test then fails, and the browser's console error names the new hash to put in both places.
