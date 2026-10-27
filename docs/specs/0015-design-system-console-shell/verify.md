# Verify: Design system and console shell · spec 0015 · updated 2026-10-27

_The screen reader script (SRT-1 to SRT-12) for AC-22, set up by milestone 5 (build plan step 24). A person runs it,
with a real screen reader, in both pairs. `/check verify` checks that every step below has a result in both pairs
before the feature is `done`. Other verify steps join this file when `/check verify` runs._

## How to run it

1. Start the gallery: from `console/`, run `pnpm dev`, then open `http://localhost:5173/console/gallery.html`.
2. Run every step at 1280 px wide, then again at 800 px wide (the gallery toolbar's width frame, or resize the window).
3. In each pair, record the browser, screen reader, and OS versions below, then pass or fail per step. For a failure,
   write what you heard and the fix (a commit or issue link), and run the step again after the fix.

The shell scenes are `#/acme/storefront` (project scope, with a banner), `#/acme/storefront/databases` (with the
resource list), `#/acme` (org scope), and `#/account/profile` (account scope). The primitives are under `#/overlays`
(`Dialog`, `SideSheet`), `#/feedback` (toasts), and `#/data` (`CopyField`).

## Pairs

| Pair | Browser and version | Screen reader and version | OS and version | Run by, date |
| ---- | ------------------- | ------------------------- | -------------- | ------------ |
| A    | Safari (owed)       | VoiceOver (owed)          | macOS (owed)   | owed         |
| B    | Firefox (owed)      | NVDA (owed)               | Windows (owed) | owed         |

## Script

Each step lists what you should hear. Mark A and B with pass or fail.

| Step   | Where                                  | Do and expect                                                                                                                                                | A    | B    | Fix |
| ------ | -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---- | ---- | --- |
| SRT-1  | `#/acme/storefront`                    | The first Tab reaches "Skip to content"; activating it puts focus on `main`, which is announced.                                                             | owed | owed |     |
| SRT-2  | `#/acme/storefront`                    | The landmarks list (VO rotor, NVDA D) shows banner, the "Breadcrumb" navigation, the "Project" navigation, and main, each once.                              | owed | owed |     |
| SRT-3  | `#/acme/storefront/databases`          | Move through the sidebar: each item reads its name, and "current page" on Databases. Collapse to the icon rail; names are still read.                        | owed | owed |     |
| SRT-4  | `#/acme/storefront/databases/users`    | Breadcrumbs read as a list with "users" as current page. Open the project switcher, type "b", and hear the result count and each result.                     | owed | owed |     |
| SRT-5  | `#/acme/storefront`                    | Open the account menu. Theme and Density read as groups with the checked option; changing the theme is visible at once.                                      | owed | owed |     |
| SRT-6  | `#/overlays`                           | Open a `Dialog`: focus moves into it, its title is read, Tab stays inside, Escape closes, and focus returns to the opener.                                   | owed | owed |     |
| SRT-7  | `#/overlays`                           | Open the modal `SideSheet` with a form. Submit it empty: focus moves to the error summary, which reads the count and links to each field.                    | owed | owed |     |
| SRT-8  | `#/feedback`                           | Raise a success toast while focus is in a field. It is announced once, focus doesn't move, and it closes after 6 s (longer while focused).                   | owed | owed |     |
| SRT-9  | `#/data`                               | Press the copy button of a `CopyField`; "Copied" is announced once.                                                                                          | owed | owed |     |
| SRT-10 | `#/acme/storefront`                    | Press Mod+K (⌘K, or Ctrl+K). The palette opens as a dialog with a combobox; arrow keys read each action; Escape closes it and focus returns to where it was. | owed | owed |     |
| SRT-11 | `#/acme/storefront` at 800 px          | Open the sidebar drawer: focus moves in and is trapped, and the drawer's name ("Project") is read; Escape closes it and returns focus to the sidebar button. | owed | owed |     |
| SRT-12 | toolbar direction set to right to left | Repeat SRT-3 and SRT-11: the reading order follows the visual order from the right.                                                                          | owed | owed |     |

## Milestone 5 additions to listen for

Not numbered steps in the spec, but worth hearing in the same session:

- The help button reads "Help, collapsed". Opening it reads the "Help" heading inside the "Help" region (a "Help"
  dialog at 800 px); Escape returns focus to the help button.
- `?` outside a text field opens "Keyboard shortcuts", which lists each shortcut with its keys.
- The activity button reads "Activity", or "Activity, new messages" after a toast; its popover reads "No running jobs"
  and the message history.

## Acceptance criteria coverage

- AC-22: covered by SRT-1 to SRT-12 above, in pairs A and B. **Owed**: neither pair has been run yet. Pair A needs a
  person listening with VoiceOver on macOS; pair B needs Windows with NVDA, which the build machine doesn't have.
