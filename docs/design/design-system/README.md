# Orvano

**The AI-native backend you host yourself.** Orvano's interface should feel like dependable infrastructure: calm, precise and dense enough for daily use, with one cobalt accent that always means "this is the action" or "this is where you are". This system covers the Orvano dashboard (`/console`), the docs site and the CLI's visual output.

## Principles

1. **Quiet by default, cobalt for intent.** Neutral slate surfaces carry the page. `brand` appears on the primary action, the active nav item, links and focus. That is one primary button per view, never decoration.
2. **Dense, not cramped.** Developers scan tables of users, rows, deliveries and logs all day. Use 14px body text, 36px table rows and a 4px grid (`space-1` … `space-12`). Prefer more rows on screen over generous padding.
3. **Borders, not shadows.** Cards and tables are drawn with `border` hairlines on `surface`. `shadow-popover` is only for things that float (menus, popovers, the command palette, toasts).
4. **Machine-readable things look machine-readable.** IDs (`prj_01JF…`), keys, URLs, SQL, event names and code use `mono` / `mono-small` in IBM Plex Mono, with a copy button on hover.
5. **Status is never colour alone.** Every state pairs a colour with a word and/or icon: a dot plus "Deployed", or a warning icon plus "Pending approval".
6. **Agents are visible.** Anything an AI agent did or wants to do carries the agent's name and a "via MCP" tag. Pending approvals use `warning` and are always one click from anywhere (top-bar badge).

## Voice and content

- Plain, direct, second person. "Your function deployed." not "Function deployment was successful."
- Sentence case everywhere: buttons, headings, menu items. "Create bucket", not "Create Bucket".
- Buttons say the verb and the object: "Apply migration", "Revoke sessions", "Approve change".
- Errors say what happened, then what to do: "Column `autor_id` doesn't exist. Did you mean `author_id`?" They match the API's `hint` text.
- Numbers get units and are right-aligned: `1.2 GB`, `312 ms`, `14,203 rows`.
- Timestamps are relative within 24h ("3 min ago") and absolute after ("Nov 27, 14:02"), with the full ISO time in a tooltip.
- No exclamation marks, no emoji, no "Oops".

## Colour

Two themes, light first; dark follows the OS with a toggle. Every pair below has been checked at 4.5:1 or better for text.

| Role | Tokens | Rules |
| --- | --- | --- |
| Ground | `bg` → `surface` → `surface-subtle` | Page on `bg`; cards/tables on `surface`; header rows, code, hover on `surface-subtle`. |
| Lines | `border`, `border-strong` | `border` for hairlines; `border-strong` for inputs and controls (needs 3:1). |
| Text | `ink`, `ink-muted`, `ink-subtle` | Body in `ink`; metadata in `ink-muted`; `ink-subtle` only for placeholders and disabled. |
| Brand | `brand`, `brand-hover`, `on-brand`, `brand-subtle`, `brand-ink` | Primary button = `brand` fill + `on-brand` text. Selected row / active tab = `brand-subtle` + `brand-ink`. |
| Status | `success`, `warning`, `danger` + their `-subtle` | Badges = `-subtle` background + the strong colour for text and dot. Destructive buttons use `danger` fill with white text. |

Do not invent new hues. If a chart needs a series palette, use `brand` first, then `ink-muted`, then the status colours, and label every series directly.

## Typography

- **IBM Plex Sans** for all UI and docs text; **IBM Plex Mono** for code, IDs, keys and logs. Both load from Google Fonts (weights 400, 500, 600).
- Scale: `display` 32/40 (setup and empty-state titles only), `h1` 24/32 (page title), `h2` 18/26 (section), `h3` 15/22 (card title), `body` 14/20, `small` 13/18, `caption` 12/16 medium (labels, table headers in sentence case, never all caps).
- Weights: 400 body, 500 for emphasis and labels, 600 for headings. Never bold body paragraphs for emphasis; use `body-strong`.

## Layout

- **App shell:** a 56px left rail of service icons with labels on hover (Overview, Auth, Database, Storage, Realtime, Functions, Sites, Webhooks, Email, AI, Logs, Settings). A 48px top bar holds the instance → project switcher on the left and, on the right, the approvals badge, the `⌘K` command palette trigger, the theme toggle and the user menu.
- **Page:** `space-6` gutters, max content width 1280px for forms and settings; tables and editors go full width.
- **Page header:** `h1` title, one-line description in `ink-muted`, primary action at the right.
- **Tables:** 36px rows, `caption` headers on `surface-subtle`, `border` row dividers, sticky header, right-aligned numbers, mono IDs. Row click opens a side sheet (480px), not a new page.
- **Forms:** labels above fields, 36px controls, `space-4` between fields, help text in `small` `ink-muted`. Destructive confirmations ask the user to type the resource name.
- **Empty states:** `display`-free; an `h2`, one sentence, one primary action, and a code snippet or "Ask your AI agent" MCP prompt when relevant.

## Components

The dashboard is built on shadcn/ui (Radix + Tailwind). Map shadcn's CSS variables to these tokens (`--background` → `bg`, `--card` → `surface`, `--primary` → `brand`, `--primary-foreground` → `on-brand`, `--muted` → `surface-subtle`, `--muted-foreground` → `ink-muted`, `--border` → `border`, `--input` → `border-strong`, `--ring` → `brand`, `--destructive` → `danger`, `--radius` → `radius-md`).

- **Button:** primary (`brand`), secondary (`surface` + `border-strong`), ghost, destructive (`danger`). Heights 32 and 36. `radius-md`.
- **Badge:** `-subtle` background, strong colour text, optional 6px dot, `radius-sm`, `caption` type.
- **Status dot:** 8px `radius-full` in a status colour, always followed by a word.
- **Approval card:** `warning-subtle` banner with the agent's name, the planned change as a diff (SQL or JSON in `mono`), and two buttons: "Approve" (primary) and "Reject" (secondary).
- **Code block:** `surface-subtle`, `mono`, `radius-md`, copy button top-right, language label in `caption`.
- **Command palette:** `surface`, `radius-lg`, `shadow-popover`, on `overlay`.

## Iconography

- **Lucide** icons at 16px (inline) and 20px (rail), 1.5px stroke, `currentColor`. Never filled icons or emoji.
- Service icons: Auth `key-round`, Database `database`, Storage `folder`, Realtime `radio`, Functions `square-function`, Sites `globe`, Webhooks `webhook`, Email `mail`, AI `sparkles`, Logs `scroll-text`, Settings `settings`.

## Logo

There is no logo yet. Until one exists, set "Orvano" in IBM Plex Sans 600 in `ink`, with a 10px `brand` square (`radius-sm`) before the word as a placeholder mark. Replace it here, and add the files under `assets/Logos/`, once a mark is designed.

## Motion

120–160ms ease-out for hovers, popovers and side sheets; no bouncing or decorative motion. Respect `prefers-reduced-motion`.
