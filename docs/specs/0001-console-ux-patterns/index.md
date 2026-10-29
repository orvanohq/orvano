# 0001. Console UX patterns

**Date**: 2026-10-15
**Status**: Accepted
**Updated**: 2026-10-28, reconciled with spec 0015 after feature 11: UX-25 names the palette's menu pattern and the local action subset until feature 25; the grid is react-data-grid, built by the rows feature; the feature 11 follow up waits only on spec 0015 AC-22.

## Summary

This spec sets the layout and interaction rules every Orvano console screen follows: a two level sidebar with a resource list (like your table list) that stays visible while you work, tabs that are real links, forms that open in side sheets, a spreadsheet style grid, and clear rules for empty states, loading, errors, deleting, and messages. Each rule has an ID (`UX-1` and up) so builds and checks can point at it. The design system (feature 11) turns these rules into shared components, and every console screen is built from those components, so the rules hold without anyone having to remember them.

## Requirements

This is a cross cutting standard, so it has no build plan of its own. The rules in **Standard definition** are the contract: every console screen must meet each rule that applies to it, and `/check verify` checks a screen against the IDs. Each console screen's own spec lists the UX IDs it touches, so it's always clear which rules apply. The visual language (color, type, spacing, themes) belongs to feature 11, and the code conventions (routing, data fetching, state) belong to feature 2.

**Applies to:** every screen in the Orvano console (the React app built with Vite), across every service: Auth, Databases, Storage, Realtime, Functions, Sites, Messaging, operations, and settings. It must meet WCAG 2.2 AA and keep every string translation ready.

**User stories**:

- As a developer, I want to keep my table list in view while I work inside a table so I can jump between tables without losing my place.
- As a developer, I want links, refresh, and the back button to bring me back to exactly the view I was in (tab, filters, sort, open row).
- As a developer, I want to fix a cell right in the grid, undo it if I got it wrong, and never silently overwrite a teammate's change.
- As a developer, I want deleting a whole project to be hard to do by accident, and deleting one row to be quick.
- As a keyboard or screen reader user, I want every screen, sheet, grid, and dialog to work without a mouse.

## Decision

**Chosen option**: Option 1: Document the standard, enforce it through shared primitives, an import lint rule, and automated plus scripted accessibility checks.

Every console screen is composed from the design system primitives listed below; the rules live inside those primitives, so following the standard is the easy path. A lint rule (set up in feature 7) blocks importing the underlying component library outside the design system, and automated accessibility checks fail CI. Rules that tooling can't check go in a PR checklist and `/check verify`.

## Rationale

The reasoning, the alternatives for each pattern, the research, and the cross check results are in [rationale.md](rationale.md).

## Standard definition

### Layout and navigation

- **UX-1 Shell.** Three regions: a top bar, a primary sidebar, and the content area. The top bar holds the breadcrumbs (UX-4), the activity indicator (UX-29), the help button (UX-31), and the account menu. The primary sidebar lists the project's services (Overview, Auth, Databases, Storage, Realtime, Functions, Sites, Messaging, then usage, logs, and settings). Navigation never goes deeper than two levels: services, then resources. Settings sections (members, domains, API keys, webhooks, email, and so on) are page tabs (UX-5), not sidebar items.
- **UX-2 Resource layout.** Every service that holds a collection uses the resource layout: a secondary sidebar listing its resources (tables in a database, buckets, functions, sites, messaging topics and providers) next to the open resource.
  - The list stays visible while a resource is open, and the open one is highlighted and marked `aria-current="page"`.
  - A filter box and a create button sit at the top. In Databases, a database picker sits above the table list.
  - The list collapses (to give the grid room) and resizes between 200 and 480 px. The resize handle is a focusable separator, so arrow keys can resize it too (no dragging needed, WCAG 2.5.7). Collapsed state and width are remembered per service (UX-28).
  - Long names truncate visually, but the full name stays the accessible name and shows in a tooltip (UX-40).
  - Auth users and logs are lists of records, not resource lists. They follow the grid and table rules (UX-11).
- **UX-3 Narrow widths and zoom.**
  - From 1024 px wide the full layout shows; you can still collapse the primary sidebar to an icon rail, with labels in tooltips (UX-40). Below 1024 px, both sidebars become drawers (they open over the content, trap focus, and close with Escape).
  - The grid content area is never narrower than 480 px. When the open panels would squeeze it below that, the resource list collapses and the row sheet (UX-13) opens over the grid.
  - Tabs that don't fit scroll sideways, keeping the focused tab in view.
  - At 320 px wide (400% zoom), every part of the console reflows except grids, which may scroll both ways. Data tables are allowed to do that under WCAG 1.4.10.
  - Small screens are for checking and light edits, not heavy editing.
- **UX-4 Breadcrumbs and switchers.** The top bar shows `Org / Project / Service / Resource`.
  - Pages that aren't a resource (Overview, usage, logs, settings) use the page title as the last crumb.
  - The org and project crumbs open a switcher that searches on the server and shows up to 20 results.
  - Below 1024 px, the middle crumbs collapse into a menu.
  - The document title mirrors the crumbs in reverse: `Resource · Service · Project · Orvano`.
- **UX-5 Tabs.** Tabs inside a resource (for a table: Rows, Columns, Indexes, Permissions, Settings) are links styled as tabs, each its own route, with `aria-current="page"` on the active one.
  - Opening a resource with no tab in the URL redirects to the first tab by replacing the history entry, so Back doesn't loop.
  - Filters, sort, and paging in the URL belong to the Rows tab only and don't carry over to other tabs.
  - Real ARIA tabs (the APG tabs pattern) are only for switching views inside one panel, such as the SDK picker in UX-37. If their panels load content, they use manual activation.

### Forms

- **UX-6 Where forms open.**
  - **Side sheet** (from the right): create or edit one resource with at most 8 fields and at most 2 sections, so the list stays visible. Create and edit sheets are modal: focus is trapped, the page behind stays visible but inert, and Escape closes (asking first if there are unsaved changes, UX-9). The row sheet (UX-13) is the only sheet that isn't modal.
  - **Full page**: anything larger, and every wizard (new function, new site, new project, messaging provider setup).
  - Editing an existing resource uses the same surface as creating it.
  - **Modal dialog**: only confirmations (UX-23) and forms of one or two fields, such as rename.
  - A sheet never opens another sheet. The only dialog that can open over a sheet is a discard or confirm dialog, and focus returns to the sheet when it closes.
- **UX-7 Fields.** Labels sit above fields, and help text sits under the label. Since most fields are required, mark the optional ones with "(optional)" instead of starring the required ones. A placeholder never replaces a label.
- **UX-8 Validation.**
  - A field is checked when you leave it after editing, never while you're still typing. Once a field shows an error, it's checked again on every change, so the error clears as soon as it's fixed.
  - Checks that need the server (for example, "name already taken") run only when you leave the field. They show "Checking" in a polite live region, and the server has the final say on submit.
  - On submit, an error summary appears at the top of the form. Focus moves to it, and each item links to its field. The message beside each field uses the same words.
  - Server errors that name a field show at that field, and the rest go in the summary.
  - The submit button is never disabled just because a field is invalid (pressing it shows the errors). While saving, it shows a busy state that's announced, and a second press does nothing.
  - Error text is specific and polite, and color always comes with an icon or text.
- **UX-9 Saving and unsaved changes.**
  - Settings are grouped into cards. Each card has its own Update button that turns on only when the card has changes, meaning a value differs from what was last saved (changing a value and then changing it back clears this). Success shows a toast, and errors show inside the card.
  - Every form, sheet, card, and full page form uses one shared unsaved changes guard. Moving to another page asks before throwing changes away, and closing the browser tab triggers the browser's own warning.
  - Destructive settings live in a Danger zone card at the bottom of the page.
- **UX-10 No repeated typing.** Forms fill in anything already known (WCAG 3.3.7). A failed submit keeps everything you entered, including values that were wrong. The one exception is the password field after a failed sign in, which is cleared.

### Data grid

- **UX-11 Grid or table.** Screens where you edit records (table rows) use an editable grid. Lists you only read (Auth users, logs, deployments, files in list view) use a plain table, and their row actions go in a menu.
  - Editable grids follow the APG grid pattern. The grid is one tab stop. Arrow keys move between cells, Home and End move within a row, Ctrl+Home and Ctrl+End jump to the first and last cell, and Page Up and Page Down move a page of rows. Enter or F2 starts editing, and Escape stops.
  - On a header cell, Enter (or Shift+F10) opens that column's menu.
  - The grid virtualizes rows and columns (draws only what's on screen) and sets `aria-rowindex` and `aria-colindex`.
  - A cell shows at most 200 characters; anything longer ends in an ellipsis and opens in full in the row sheet.
- **UX-12 Inline editing.** Simple values (text on one line, number, boolean, date and time, enum) are edited right in the cell. JSON, long text, files, relations, and arrays open in the row sheet (UX-13).
  - A change saves when you press Enter or leave the cell, and Escape cancels it. The cell shows that it's saving.
  - Every save sends the row's version. If someone changed the row since you loaded it, the save is refused and the cell offers **Reload** or **Overwrite**.
  - If the server rejects a change, the cell goes back to its old value and shows the error inline at the cell.
  - Undo reverts committed changes, up to the last 20, until you leave the table. Use the Undo button in the grid toolbar or Mod+Z. Mod+Z works this way only while you're moving between cells; while you're editing a cell, it undoes typing as usual.
  - Undo is refused, with a message, if someone else changed the row after your edit.
  - Pasting into a range of cells counts as one change for undo.
  - While any row is selected (UX-17), the grid can't be edited.
- **UX-13 Row sheet.** Opening a row shows every field, the row ID and timestamps (with copy buttons), and related rows as links.
  - Previous and Next buttons move between rows.
  - The sheet edits with its own Save and Cancel footer. If you have unsaved changes and move to another row, it asks first.
  - It's 400 to 560 px wide and doesn't trap focus, so you can click another row while it's open. It never covers the focused element (WCAG 2.4.11); see UX-3 for narrow widths.
- **UX-14 Paging.** Paging uses Previous and Next buttons with cursors (a cursor is a bookmark the server returns, and it hands back one for each direction).
  - There's a page size picker (25, 50, or 100, default 50). Changing the page size goes back to the first page.
  - The total count shows only when the API returns it.
  - Paging controls sit below the grid, and the header row stays fixed while you scroll.
  - Grids never use infinite scroll.
  - A query that hits the server's time limit shows "This query is slow. Add an index" with a link to the Indexes tab.
- **UX-15 Sort and filter.**
  - Sort is set from the column header menu with three states: ascending, descending, and default. Default order is the primary key, ascending. Sorting always breaks ties by primary key, and only one column sorts at a time.
  - The filter builder adds rules (column, an operator that fits the column type, a value), joined with AND, up to 10 rules. Feature 25 defines the operators for each column type.
  - Active filters show as chips you can remove one by one, plus a Clear filters button.
  - In the URL, each rule is a repeated `filter` parameter in the form `column:operator:value`, percent encoded (UX-27). A rule naming an unknown column becomes a dismissible "ignored filter" chip, never an error page.
- **UX-16 Columns.** A columns menu shows and hides columns. You can resize and reorder columns by dragging, and every drag also has a menu alternative (Set width, Move left, Move right) for WCAG 2.5.7. Column visibility, width, and order are remembered per table (UX-28).
- **UX-17 Selection and bulk actions.**
  - A checkbox column selects rows, and the header checkbox selects the whole page.
  - "Select all N matching" appears only when the count is known. It's tied to a snapshot of the filters at the moment you select, and any confirm dialog shows those filters.
  - While rows are selected, a bar above the grid shows the count and the bulk actions (delete, export, update a field), and the actions on each row are disabled.
  - Bulk actions are all or nothing: if any row fails, nothing changes, and the error lists up to the first 5 rows that failed.
  - Escape or Clear selection leaves bulk mode.
- **UX-18 Clipboard.**
  - Copying a cell or a range of cells puts it on the clipboard as tab separated values.
  - Pasting into a range of cells you can edit saves it as one all or nothing change, up to 1,000 cells.
  - If any pasted value doesn't fit its column type, the whole paste is rejected and the bad cells are highlighted.
- **UX-19 Live updates.** Once realtime ships (feature 34):
  - Changes made elsewhere never move rows under your cursor or touch a cell you're editing.
  - New rows that match the active filters show as an "N new rows" pill at the top, and clicking it loads them.
  - Changes to rows on screen update in place with a brief highlight. A row that no longer matches the filters is dimmed and marked "no longer matches" until you refresh.
  - Deleted rows are dimmed and marked as deleted until you refresh.
  - Until realtime ships, every list has a Refresh button.
  - Coming back to the browser tab reloads the current page only if its data is over 30 seconds old, and never while you're editing a cell, a sheet has unsaved changes, or rows are selected. A reload keeps your selection by row ID.

### Row creation

- **UX-38 Insert row.**
  - Insert row opens the create sheet. A table with more than 8 columns opens a full page instead (UX-6).
  - Required columns come first, and every default value is shown, so you can see what the server will fill in.
  - Values are checked against the column types (UX-8).
  - Relations use a picker that searches the related table, files use the file picker, and JSON uses a JSON editor that checks syntax.
  - After saving, the new row is highlighted in the grid.

### States

- **UX-20 Empty states.** There are three kinds:
  - **First use**: a short positive title, one sentence, one primary action, and an SDK quick start (UX-37).
  - **No results**: say so, and offer Clear filters.
  - **Not allowed or not set up**: give the reason and the next step.
    An empty grid drops its header row, and illustrations are decorative (`aria-hidden`).
- **UX-21 Loading.**
  - Content areas (grids, lists, cards, detail panels) show a skeleton of their layout if loading takes longer than 300 ms. Once shown, the skeleton stays for at least 500 ms so it doesn't flicker. Spinners appear only inside buttons and small inline controls.
  - When you page, filter, sort, or refetch, the current data stays on screen with a busy indicator (`aria-busy`). Data kept on screen this way is tied to its resource, so opening a different table never shows the previous table's rows.
  - Changing filters, sort, page, or route cancels any request still in flight that's no longer needed.
  - Server jobs (UX-29) and uploads that take longer than 10 seconds show progress (with a percentage when it's known) and a Cancel button where they can be cancelled. Ordinary requests time out after 30 seconds and show the error state (UX-22).
- **UX-22 Errors.**
  - If a page fails to load, it shows what failed, a Retry button, and the request ID from the API response (copyable).
  - A missing resource shows "not found" with a link back to its list.
  - A permission denial names the role needed when the API returns it, and otherwise says "You don't have access".
  - If your session expires mid edit, your form is kept, a sign in dialog opens over the page, and the action retries once you're signed in.
  - Too many requests shows when you can try again.
  - A server error while loading a page shows the page error. A server error from an action shows inline where the action happened.
  - Losing the network shows a banner until the connection is back.
  - Reads retry automatically twice, waiting longer each time. Writes never retry automatically.

### Destructive actions

- **UX-23 Tiers by blast radius.** `ConfirmDelete` holds the map from resource type to tier, and a resource type that isn't in the map fails the build.
  - **Tier 1, type to confirm**: anything that holds other resources holding data (organization, project, database, bucket). The dialog lists what goes with it. The delete button stays off until you type the exact name, and its label is specific ("Delete project").
  - **Tier 2, confirm**: every other resource (table, column, index, function, deployment, site, domain, API key, webhook, messaging provider or topic, end user, console member, backup, file). The dialog names the item and lists what goes with it (for a column, the number of rows that lose the value). The button label is specific ("Delete table"), and focus starts on Cancel.
  - **Single row**: a short Tier 2 confirm with a "Don't ask again for this table this session" checkbox.
  - **Bulk**: a Tier 2 confirm that states the count ("Delete 37 rows"). Over 1,000 rows, you type the count to confirm.
  - **What goes with it**: these numbers come from each resource's dependents endpoint (feature 10). Estimated counts are labeled "about". While the counts load, the dialog shows a loading state. If counting fails, it says "Couldn't count what will be deleted" and still lets you delete.
  - Destructive buttons use the danger style and never sit next to the primary action.

### Messages

- **UX-24 Where messages go.**
  - **Inline messages** go next to the field, card, cell, or panel they belong to. They're used for every error whose source is on screen, use `role="alert"`, and never close on their own.
  - **Toasts** (bottom right, at most three, newest on top, older ones queued) are for success and background results. They use `role="status"` and never take focus. Success toasts close after 6 seconds, and the timer pauses while the pointer or focus is on them. An error whose source isn't on screen (a failed deploy while you're elsewhere, a failed copy) is a toast that stays until dismissed. Identical toasts merge and show a count. Toasts never cover the focused element.
  - **Banners** sit under the top bar for state that affects the whole project (limits reached, service degraded, offline). They show one at a time and stay until the state clears.
  - **Message history**: the activity panel (UX-29) keeps the last 50 messages of the session, so a toast that closed can still be read (WCAG 2.2.1).
- **UX-41 One announcer.** Every screen reader announcement (copied, saved, checking, loading done) goes through one shared announcer region in the shell, so nothing is announced twice.

### Power features

- **UX-25 Command palette.** Mod+K opens the palette.
  - Results come from a server search over named resources (orgs, projects, services, tables, buckets, functions, sites; never rows, files, or users), up to 20 at a time, updated 150 ms after you stop typing. The current project comes first, then the org, then your account. Recent items show first and are remembered (UX-28).
  - Actions include create table, bucket, or function; copy the API endpoint or project ID; switch theme; and open docs. An action that needs input opens its create sheet.
  - It follows the APG dialog pattern plus the combobox pattern, with a menu of actions and results (spec 0015 AC-13). Escape closes it and returns focus to where you were.
  - Until feature 25 adds the server search and the create and copy actions, the palette lists local actions only: go to each nav item, switch theme or density, show shortcuts, open help (spec 0015 SHL-10).
- **UX-26 Keyboard shortcuts.**
  - Shortcuts that work everywhere use a modifier key (Mod+K). Single key shortcuts work only while their component (the grid, for example) has focus.
  - `?` opens a list of shortcuts, except while you're typing. Targets declare their shortcuts with `aria-keyshortcuts`.
  - Every shortcut action can also be done with a visible control.
- **UX-27 URL state.**
  - The path holds where you are: org, project, service, resource, tab.
  - The query string holds filters, sort, page cursor, page size, the open row ID, and an open create sheet.
  - Refresh, Back, and a pasted link all restore the same view.
  - History: changing the path, sort, page, or page size, and opening a row or sheet, each add a history entry. Typing a filter value, moving between rows inside the row sheet, and the default tab redirect replace the current entry.
  - An expired or invalid cursor falls back to the first page with a toast ("This link's page expired, showing the first page"). A row ID that doesn't exist is removed from the URL with a toast.
  - The console sends `Referrer-Policy: same-origin` so values in the URL never leak to other sites.
- **UX-28 Personal layout preferences.** These are saved in browser storage under keys like `orvano:v1:{userId}:{projectId}:table:{tableId}` (spec 0003 STO-2 lists every key). They cover sidebar collapsed state and width; each table's column visibility, width, and order; density; the last SDK you picked; and palette recents.
  - Columns are stored by column ID, not by name. Unknown IDs are ignored, and new columns appear visible at the end.
  - A version mismatch or corrupt data falls back to the defaults. Only the 50 most recently used tables per project are kept.
  - If browser storage isn't available, preferences live in memory for the session.
  - Theme is also stored under one global key, so the sign in page shows the right theme.
  - Preferences never hold secrets or project data.
- **UX-29 Long running work** (deploys, builds, backups, imports).
  - Items show their status as a chip: queued, running, ready, failed, cancelled.
  - The detail view streams logs live and scrolls along with them, but stops following when you scroll up, and a "Jump to latest" button picks it back up.
  - The log view keeps the last 5,000 lines on screen, drawing only the visible ones, with an "earlier lines hidden, download the full log" row above them. The log region is not a live region, so lines aren't read out one by one.
  - The activity indicator in the top bar lists the project's running jobs (from the API's active jobs list), with links to each, and the message history (UX-24).
  - The person who started a job gets a toast when it finishes while they're on another page.
- **UX-30 Access.** The API returns, for each resource, the actions you're allowed to take, and for each project, the services you can open. The console never hard codes role names.
  - Services you can't open are hidden from the sidebar.
  - Actions you can't take on a screen you can see stay visible but disabled. Use `aria-disabled` so they stay focusable, and show a hint naming the role you need when the API provides one.
  - Until team roles ship (feature 51), every action is allowed.
- **UX-31 Help.** The help button sits in the same place in the top bar on every page (WCAG 3.2.6). It opens the docs in a right side pane, so your form or grid stays where it is.
  - Help links inside pages open the pane at the right doc.
  - The pane has its own Back button and an "Open in new tab" link.
  - The docs ship with the install so the pane works offline. If a page can't load, the pane shows a plain link instead.
- **UX-32 Density.** Comfortable is the default (40 px grid rows), and each user can switch to compact (28 px) for grids and lists. Forms don't change with density. Every click target is at least 24 by 24 px in both densities (WCAG 2.5.8), checkboxes inside cells included.
- **UX-33 Copy and secrets.** IDs, endpoints, and keys have a copy button that announces "Copied" through the announcer (UX-41). A secret (an API key or webhook signing secret) is shown in full once, when it's created, with a copy button and a warning. After that, only its prefix and last 4 characters show, there's no copy button, and Regenerate (Tier 2) replaces it.
- **UX-34 Time, numbers, text.**
  - Times show as relative ("3 min ago", "in 5 min") for up to 7 days and as a date after that. They're marked up with `<time datetime>`, show the exact local time and UTC on hover and focus, and refresh every minute.
  - Dates and numbers use the user's locale format, and sizes use binary units (KiB, MiB).
  - Every string lives in message files that support plural forms, with none written into components.
  - Layout uses logical directions (start and end, not left and right) so it works right to left. Text in buttons and labels can grow by 30% without breaking the layout.
- **UX-35 Focus.**
  - Changing the path moves focus to the new page's main heading, except when you followed a tab (UX-5) or a resource list link (UX-2): the frame around those stays, so focus stays on the link you used. Changing only the query string (filters, sort) leaves focus where it is, and paging moves focus to the grid. (Amended 2026-10-15 by spec 0003, RTE-7.)
  - Sheets and dialogs follow the APG dialog pattern: on open, focus goes to the first field, or to the title for read only content. On close, focus goes back to the control that opened it, or to the list heading if that control is gone.
  - The fixed header, the fixed paging bar, and toasts never fully hide the focused element (WCAG 2.4.11). Scroll padding takes care of this.
- **UX-36 Consistent page anatomy.** Every page follows the same order: breadcrumbs, then the page title with its primary action on the right, then the tabs, then the content.
  - Each surface (page, sheet, dialog) has at most one primary style button, and it's the rightmost.
  - Sheet and dialog footers put Cancel first, then the primary action, aligned right.
- **UX-37 SDK snippets.** Wherever the console shows how to do something in code (first use empty states, the project Connect panel, after creating an API key, a table's API view), it shows the SDKs behind a platform picker, with a copy button.
  - The picker defaults to JavaScript, then remembers the last SDK you chose.
  - Snippets are filled in with the real endpoint and project ID, and use a `<YOUR_API_KEY>` placeholder, never a real secret.
  - Server only operations show only the server SDKs (Dart, JavaScript server, .NET).
  - Snippets are generated from the API contract (feature 10). An SDK without a generated snippet for that operation is left out of the picker, never shown broken.
- **UX-39 Motion and contrast.** Animations (sheet slide, highlights, skeleton shimmer, toasts) respect `prefers-reduced-motion`. The console works in forced colors mode, and both themes are contrast checked in both densities.
- **UX-40 Tooltips.** Tooltips open on hover and on focus. They can be dismissed with Escape, stay open while the pointer is over them, and don't disappear on their own (WCAG 1.4.13). Essential information never lives only in a tooltip.

### Primitives the design system must provide (feature 11)

`AppShell` · `Breadcrumbs` (with `OrgSwitcher`, `ProjectSwitcher`) · `ResourceLayout` + `ResourceList` · `RouteTabs` · `Tabs` · `PageHeader` · `SideSheet` (modal, and non modal for the row sheet) · `Dialog` · `Form`, `Field`, `ErrorSummary`, `UnsavedChangesGuard` · `SettingsCard` · `DangerZone` · `DataGrid` (with `FilterBuilder`, `FilterChip`, `ColumnMenu`, `BulkActionBar`, `Pagination`) · `Table` · `EmptyState` · `Skeleton` · `Progress` · `ErrorState` · `ConfirmDelete` · `Toast` · `Banner` · `Announcer` · `CommandPalette` · `ShortcutHelp` · `ActivityIndicator` (with message history) · `LogStream` · `StatusChip` · `CodeSnippet` · `CopyField` · `SecretReveal` · `DocsPane` · `RelativeTime` · `Tooltip` · `Menu` · `Popover` · `Select` / `Combobox` · `Checkbox` · `Radio` · `Switch` · `RelationPicker` · `JsonEditor`. Any other component in feature 11's set is covered by the same rules and the same import restriction.

**Canonical pattern** (shape only. Feature 2 picks the router and libraries, and this shows how a screen is composed):

```tsx
// Route: /:orgId/:projectId/databases/:databaseId/tables/:tableId/:tab?filter&sort&cursor&row
export function TablesSection() {
  return (
    <ResourceLayout // UX-2: list stays visible
      list={
        <ResourceList
          kind="table"
          picker={<DatabasePicker />}
          createLabel={t("tables.create")} // UX-34: no hard coded strings
          onCreate={() => openSheet("create-table")} // UX-6 + UX-27: sheet state in the URL
        />
      }
    >
      <PageHeader title={table.name} primaryAction={<InsertRowButton />} /> {/* UX-36, UX-38 */}
      <RouteTabs tabs={["rows", "columns", "indexes", "permissions", "settings"]} /> {/* UX-5 */}
      <Outlet />
    </ResourceLayout>
  );
}

// Destructive action: the tier comes from the resource type map, never hand rolled.  UX-23
<ConfirmDelete kind="database" resource={database} />; // fetches dependents, applies tier 1
```

**Replaces** (all of these are wrong in the console):

- Hand built modals, drawers, tab widgets, tooltips, or tables outside the design system.
- A resource list that disappears when one item opens, or a third level of navigation.
- Tabs that don't change the URL for resource views, and filters or sort kept only in memory.
- Disabling submit until a form is valid, and checking fields while the user types.
- Generic "Are you sure?" dialogs with Yes and No buttons, and tiers picked by hand.
- Saving a cell without a version check (last write silently wins).
- Spinners filling content areas, and infinite scroll in grids.
- Toasts that close themselves for errors.

**Enforcement**:

- **Shared primitives.** Screens use only the primitives above. The underlying component library (feature 2 or 11 picks it) may be imported only inside the design system package, and an import lint rule (feature 7) fails CI otherwise.
- **Strings.** A lint rule flags literal text in JSX outside message files (with a short allow list, such as code snippets and brand names) and fails CI.
- **Automated accessibility checks** run in component tests and end to end tests and fail CI on WCAG 2.2 AA violations. They catch only part of WCAG, so:
- **Scripted screen reader and keyboard test.** For each release of a complex primitive (`DataGrid`, `SideSheet`, `Dialog`, `CommandPalette`, `RelationPicker`), a written test script runs with two screen reader and browser pairs (NVDA with Firefox, VoiceOver with Safari). The results are recorded in feature 11.
- **PR checklist and `/check verify`.** Each screen's spec lists the UX IDs it touches. Reviews and `/check verify` check the rules tooling can't (UX-6 surface choice, UX-36 anatomy, UX-38).

**Rollout**: new code immediately. No console code exists yet, so nothing needs migrating.

**Exceptions**:

- The SQL editor (feature 27) and code editors have their own keyboard model inside the editor area. The page around them still follows the standard.
- Pages without the shell (first run setup, console sign in and sign up) follow the form, focus, and message rules but not UX-1 to UX-4.
- The public docs site (feature 32) is outside this standard.

## Consequences

**Positive**:

- Every service screen feels the same, so each new service (functions, sites, messaging) mostly composes existing primitives instead of designing new ones.
- Links, refresh, and Back always restore the view, which makes support and teamwork easier ("here's the filtered view").
- Version checks on every save mean two people (or two tabs) never silently overwrite each other.
- Accessibility is designed into the primitives and tested with real screen readers, not checked afterward, which keeps WCAG 2.2 AA realistic for a small team.

**Negative / tradeoffs**:

- The grid is the most expensive part: the APG keyboard model, virtualization, inline editing with version checks and undo, range copy and paste, column reorder with alternatives to dragging, and live updates. Building it by hand is a large job. Feature 11 chose react-data-grid (spec 0015 PRM-5); wrapping it to meet UX-11 to UX-19 is the rows feature's (16) work and still takes real effort.
- Several rules need API support that doesn't exist yet: dependents counts, row versions, cursors in both directions with an optional count, error responses that name the field and the request ID, allowed actions per resource, a search endpoint, an active jobs list, and all or nothing bulk operations. A console screen can't fully meet its rules until those land, so early slices may meet some rules only partly.
- Saving preferences in the browser means they don't follow a user to another device or browser. Moving them to the server later needs an API and a migration of the stored keys.
- Bundling the docs with the install makes the self hosted package bigger and ties docs updates to Orvano releases.

**Neutral**:

- The console looks and behaves much like Appwrite and Supabase, which lowers the learning curve for developers who switch, but it isn't yet a point of difference.
- The rule IDs (UX-1 to UX-41) become the shared vocabulary for `/check verify` and code review of console work.

## Follow-up

- [ ] Feature 11 (design system & console shell): build the primitives listed above, pick the grid and component library, set exact row heights and contrast for both themes, and run the screen reader test script. Built and decided in spec 0015 (React Aria Components with Tailwind CSS v4, react-data-grid, the row heights and contrast in `console/design.md`); `DataGrid` and the non modal row `SideSheet` move to the rows feature (16), `LogStream` to feature 46, `JsonEditor` and `RelationPicker` to features 24 and 16. Open only until spec 0015 AC-22, the screen reader run, is recorded in its `verify.md`.
- [ ] Feature 2 (React console practices): cover nested route layouts for UX-2 and UX-5, keeping URL query state in sync with the history rules in UX-27, cancelling requests (UX-21), and safe access to browser storage (UX-28).
- [ ] Feature 10 (API contract & SDK generation): define an error format that names the field, the request ID, and the required role (UX-8, UX-22); a dependents endpoint for each deletable resource (UX-23); cursors in both directions with an optional cheap count (UX-14); allowed actions per resource and capabilities per project (UX-30); and snippet generation for all six SDKs (UX-37).
- [ ] Features 16 and 25 (rows API, queries): add row versions with a refused save on mismatch (UX-12), all or nothing bulk update, delete, and paste (UX-17, UX-18), the operators for each column type and the URL filter format (UX-15), sorts that break ties by primary key, and a query time limit (UX-14).
- [ ] Feature 25 or 46: add a search endpoint for the command palette (UX-25) and an active jobs list (UX-29).
- [x] Feature 7 (coding standards & tooling): set up the import lint rule, the literal string rule, and automated accessibility checks in CI. (Done in spec 0008: the import boundary, LNT-3; the literal string rule, LNT-2; Oxlint `jsx-a11y` in CI, LNT-4. The axe checks, spec 0003 A11Y-5, run in the Vitest browser tests once feature 11's primitives exist.)
- [ ] Feature 32 (docs site): make the docs build that ships with each install, for the docs pane (UX-31).
- [ ] Each of these features owns its own screen patterns and should spec them against this standard: the file browser and upload queue (29, 30), the permissions editor (9, 26), the log time range, search, and saved filters (46), usage charts with a data table alternative (45), schema changes with a SQL preview (24), the function runner and the variables and secrets editor (37, 39), and onboarding beyond empty states (13).
- [ ] Row import and export (CSV and JSON, column mapping, a dry run, an error report, export of the filtered columns) is enrolled as scope feature 55, from this spec.
- [ ] Later: a production versus development cue for projects, and "view as user" for testing permissions (after feature 26).
- [ ] Optional: walk through a self hosted Appwrite console to confirm how its table list sidebar behaves. The research couldn't verify it from public pages (see rationale.md).
