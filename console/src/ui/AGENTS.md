# Design system (`src/ui`)

The console's primitives, tokens, and shell. The rules are [spec 0015](../../../docs/specs/0015-design-system-console-shell/index.md); the look is [`console/design.md`](../../design.md). Cite rule IDs (PRM, TKN, STY, SHL, GAL) in reviews.

## Conventions

- Only `src/ui` imports `react-aria-components`, `react-aria`, `react-stately`, `@react-aria/*`, `lucide-react`, and `react-data-grid` (spec 0003 DIR-3, spec 0015 AC-19). Feature code uses the primitives instead.
- A primitive is a React Aria component or composition when React Aria has one (PRM-2), and takes no `className` or `style` prop: look comes from typed props such as `variant`, `size`, `tone`, and `status` (PRM-3). An icon only primitive requires `aria-label` in its type.
- Export a new primitive from `index.ts`, unless PRM-10 lists it as lazy: those are imported by path (`@/ui/command-palette`) and loaded where they first appear.
- Raw token values live only in `theme.css` (TKN-1). The token tables in `design.md` are generated from it: `pnpm fix` writes them, `pnpm check` fails when they are stale (TKN-6). Never edit those tables by hand.
- Messages go through `notify()` in `toast/` (PRM-8). AC-12 lists the allowed live regions; a new one needs a spec change first.
- Every new primitive or state gets a scene in the dev gallery (`console/gallery.html`, code in `src/gallery/`, served by `pnpm dev` at `/console/gallery.html`). Nothing outside the gallery imports `src/gallery`, and the build must never ship it (GAL-3, checked by `check-gallery-not-shipped.mjs`).

## Tests

- Each primitive has a component test with axe that runs in Chromium, Firefox, and WebKit (`pnpm test:engines` for the last two; spec 0003 QA-1, QA-4).
- `theme.contrast.test.ts` checks every contrast pair TKN-7 names; run it after you change a color.

_Drafted by /sync from the introducing change, worth a quick human pass._
