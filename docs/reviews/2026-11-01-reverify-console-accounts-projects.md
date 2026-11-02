# /check verify console accounts and projects, rerun · BLOCKED

_Feature 13, spec [0017](../specs/0017-console-accounts-projects/index.md), rerun on `bcea672` after [#170](https://github.com/orvanohq/orvano/pull/170) fixed F-1, F-2, and F-3 from the [first report](2026-11-01-verify-console-accounts-projects.md). That report stays the record of everything not repeated here._

**BLOCKED: nothing that could run failed, but `Verify it` can't be ticked yet.** AC-6 and AC-21 now pass. Of the 27 criteria, 24 are met, 3 are met here with a row a person owes (AC-5, AC-6, AC-21), and 0 failed.

The block is only the rows below, the same way feature 12's box waits on its rows. The first report's run said a tick needs no failed or blocked criterion, and `verify.md` asks for these rows before `done`.

**#170 also brought one new problem**, [R-1](#r-1-medium-focus-drops-to-the-page-on-a-slow-list): on a slow page, focus falls off the Next button. It fails no criterion, but it's a real regression, and I'd fix it before `/check review`.

Next, as a recommendation: you could run or defer the rows below, and run `/debug` on R-1. Scope: nothing ticked, and `verify.md`, the spec, and `docs/scope/platform.md` are unchanged.

## Update: the person rows, run on request

You asked this session to run VFY-2, VFY-5, and VFY-6. **VFY-2 now passes, VFY-6 passed by script, and VFY-5 can't be run from here.** So AC-5 and AC-6 are fully met, and only AC-21 still rests on a row: 26 criteria are met, 1 is met here with rows owed, and 0 failed. `Verify it` stays unticked until VFY-5 has a result or you defer it. The image is `orvano:reverify-13`, on a fresh `install.sh --local` each time.

| Row   | Result        | What happened                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| ----- | ------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| VFY-2 | Pass          | Real Safari 26.6.1 on macOS 15.7.4, driven through `safaridriver`, from the link `install.sh` printed. `orvano_session` is kept, `HttpOnly`, `SameSite=Lax`, not `Secure`. A reload stayed signed in, after the owner form and after signing in through the page. Sign out cleared the cookie. The run was driven by WebDriver, with no person watching, in Safari's automation window.                                                                                                                   |
| VFY-6 | Scripted pass | Keyboard only from setup to project delete in Chromium 156, Firefox 157, and WebKit 27.2, 12 of 12 in each: the owner form, New project, the scope checklist, both Tier confirms, both switchers, and the skip link. Every checked stop shows a focus ring, and nothing traps the keyboard. The keys list is a grid, so Tab enters once and arrow keys reach Actions. It's a script, and `verify.md` says that doesn't replace a person, so whether it counts is your call. It didn't run in real Safari. |
| VFY-5 | Not run       | NVDA needs Windows, and this Mac has no VM. I can't drive or hear VoiceOver from here. What follows is what a screen reader would be given, and it's supporting evidence, not a pass.                                                                                                                                                                                                                                                                                                                     |

What the VFY-5 evidence shows, in Chromium and WebKit:

- Form errors look sound. Focus moves to a group named "Fix this problem to continue", and the field has `aria-invalid` and its message in `aria-describedby`.
- **Listen for this at the key reveal.** Focus lands on Close in Chromium and on `<body>` in WebKit, not on the secret or its warning. The warning sits in a `status` region that appears with its text already in it, which screen readers often skip. It may not be read aloud, and VFY-5 asks for exactly that.
- After setup, focus falls to `<body>`, and nothing is announced.

Two earlier results changed with real Safari:

- F-1: `https://evil.example/landing` stays on the console in Safari too.
- F-9: opening the key sheet reported no CSP violation in real Safari, so it may be specific to Playwright's WebKit build. It stays low, and now looks smaller.

F-10 reproduced in Chromium, Firefox, and WebKit.

## What a person still owes

| Row                      | State   | What is missing                                                         | Criteria it holds up |
| ------------------------ | ------- | ----------------------------------------------------------------------- | -------------------- |
| VFY-2                    | not run | Sign in on `http://localhost` in real Safari: a reload stays signed in  | AC-5, AC-6           |
| VFY-5                    | not run | VoiceOver on Safari and NVDA on Firefox                                 | AC-21                |
| VFY-6, the person's pass | owed    | A person at the keyboard. Try paging on a slow link too, because of R-1 | AC-21                |

## What ran

Image `orvano:reverify-13`, `sha256:d3327ffb…`, built from `bcea672`, on a fresh `install.sh --local` stack, in Playwright 1.64.1's Chromium, Firefox, and WebKit. CI's main run on `bcea672` is [41372854203](https://github.com/orvanohq/orvano/actions/runs/41372854203): `ci-gate`, `console / console`, and `console / e2e` passed. Both stacks are down with their volumes, and `orvano-conformance` was left running.

**What changed since the first run**: `git diff 0d3adfc bcea672` touches only `console/`, plus the first report. Nothing changed under `server/`, `api/`, or `deploy/`. So I reran the whole console and the API sweep, 236 scripted checks. The criteria that live in the server (AC-3, AC-4, AC-7, AC-10, AC-16, AC-17, AC-20, AC-22, AC-26) stand on the first report's evidence.

## The findings #170 set out to fix

| Finding | Result | What proves it                                                                                                                                                                                                                                                                                                                   |
| ------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| F-1     | Fixed  | 22 hostile `redirect` values stayed on the console, with no request to the fake host. That's the 7 from the first run, plus harder ones the fix's tests don't list, such as `/%2F/evil.example`, `/.//evil.example`, `/@evil.example`, `HTTPS://…`, and `https:evil.example`. A real deep link still comes back with `?size=25`. |
| F-2     | Fixed  | A slow client navigation shows a skeleton inside the shell with `aria-busy`, and a slow direct load shows the shell and a skeleton, not a blank page. A fast navigation showed none. No CSP violation, and no sideways scroll at 375 wide.                                                                                       |
| F-3     | Fixed  | A different operator through the dialog gets no prompt, from a clean page or past a changed form, and lands as the new operator. Cancel can't happen any more, because there's no prompt to cancel. The first report's dirty form count also held a race in my own script, which this run removed.                               |

`javascript:` and `data:` now land on the console's home instead of leaving a signed in person on the sign in page.

## Regressions looked for

New skeletons could disturb what the first run proved, so these ran on purpose.

| Check                                             | Result                                                                                                                                  |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| First load                                        | 229.59 KB of 230, up from 229.4. About 410 B left                                                                                       |
| Static Playwright, `csp.spec.ts` included         | 70 of 70, with `csp.spec.ts` 14 of 14                                                                                                   |
| AC-27, `pnpm e2e:stack` on this image             | 2 of 2                                                                                                                                  |
| axe in both themes and both directions            | 0 violations on 7 pages, `/sign-in`, `/reset`, and the page skeleton. The frame skeleton has one best practice note, R-2                |
| The real `ar` locale on real pages                | Tabs and the grid mirror. Down, Up, End, and Home work in the Account menu, and focus returns                                           |
| CSP on the real image                             | Chromium and Firefox clean, on 10 routes and a sheet. WebKit has the same overlay report as F-9, nothing new                            |
| UX-14 past 50 rows                                | All rows reachable, Back, Forward, reload, a bad cursor, and `size=7` all pass. `scrollHeight` equals `clientHeight`. See R-1 for focus |
| Session dialog, same operator, with a slow reload | The account form retried, `401` then `200`, and kept its text                                                                           |
| Unsaved changes guard on a slow navigation        | Asks, and Keep editing keeps the text                                                                                                   |
| API sweep, AC-9 and AC-11 to AC-15, AC-25         | 87 of 87                                                                                                                                |

## New findings

### R-1 Medium: focus drops to the page on a slow list

**#170 caused this.** Press Next on a list that takes over 300 ms, and the skeleton replaces the table and its pager, so focus falls to `<body>`. A keyboard user lands back at the top of the page on every page turn.

The same steps on both images, with `Next` focused, 55 keys, and a delayed response:

| Delay   | Before #170 (`0d3adfc`) | After #170 (`bcea672`) |
| ------- | ----------------------- | ---------------------- |
| None    | `Next`                  | `Next`                 |
| 200 ms  | `Next`                  | `Next`                 |
| 1300 ms | `Next`                  | `<body>`               |

No criterion names this, so AC-21 stays met, but `pagination.tsx` is written to keep focus through a page turn, and the first run saw it hold. You could keep the current rows on screen during a page change, or move focus to the list when it loads. Local runs can't show it, because lists answer in milliseconds.

### Low severity

| ID  | Finding                                                                                                                                                                                                                                                                                                                                |
| --- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| R-2 | The frame skeleton has no level one heading, so axe's `page-has-heading-one` best practice rule flags it in all four theme and direction combinations. It's not a WCAG rule, and the state is brief.                                                                                                                                   |
| R-3 | A write that isn't wrapped in `withReauth` doesn't retry after the session dialog. The project rename I ran shows "Your changes weren't saved", keeps the typed text, and saved on a second press. That matches QRY-8's wording, which promises a retry only for wrapped writes, but the dialog's "Sign in to carry on" suggests more. |

## Still open from the first report

F-4 is still open: the overview said `50 active` with more active keys than that. F-6 is still open: focus stays on the dialog. #170 didn't touch the code behind F-5 and F-7 to F-16, and F-9 was seen again. Their severities stand.
