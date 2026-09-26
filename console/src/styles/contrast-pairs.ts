/**
 * The text and control pairs whose contrast the design system guarantees (spec 0005, Contrast
 * pairs). `contrast.test.ts` reads this list and computes every ratio from `tokens.css` in both
 * themes; add a pair here when a new token pair is used.
 */
export interface ContrastPair {
  /** The token drawn on top (text, border, or ring), without the `--`. */
  foreground: string
  /** The token it sits on. */
  background: string
  /** The WCAG minimum: 4.5 for text, 3 for control borders and focus rings. */
  ratio: 3 | 4.5
}

const text = (foreground: string, backgrounds: string[]): ContrastPair[] =>
  backgrounds.map((background) => ({ foreground, background, ratio: 4.5 }))

const graphic = (foreground: string, backgrounds: string[]): ContrastPair[] =>
  backgrounds.map((background) => ({ foreground, background, ratio: 3 }))

export const contrastPairs: readonly ContrastPair[] = [
  ...text('foreground', [
    'background',
    'card',
    'popover',
    'muted',
    'accent',
    'sidebar',
    'sidebar-accent',
  ]),
  ...text('muted-foreground', ['background', 'card', 'popover', 'muted']),
  ...text('link', ['background', 'card']),
  ...text('danger-text', ['background', 'card']),
  ...text('success-text', ['background', 'card']),
  ...text('warning-text', ['background', 'card']),
  ...text('primary-foreground', ['primary']),
  ...text('destructive-foreground', ['destructive']),
  ...text('success-foreground', ['success']),
  ...text('warning-foreground', ['warning']),
  ...text('secondary-foreground', ['secondary']),
  ...graphic('input', ['background', 'card', 'popover']),
  ...graphic('ring', ['background', 'card', 'popover', 'sidebar']),
]
