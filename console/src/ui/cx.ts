// Spec 0015 STY-3: joins class lists. Write every class whole, as a literal, so Tailwind finds it in the source.
// No clsx or tailwind-merge: primitives take no outside classes (PRM-3), so nothing needs merging.
export function cx(...classes: (string | false | null | undefined)[]): string {
  return classes.filter(Boolean).join(" ");
}

/** The keyboard focus ring (design.md _Focus_): an outline, so forced colors mode keeps it. */
export const focusRing = cx(
  "outline-none data-focus-visible:outline-2 data-focus-visible:outline-offset-2 data-focus-visible:outline-solid data-focus-visible:outline-focus",
);

/** The focus ring inside a scroll container or a dense row, inset so it isn't clipped. */
export const focusRingInset = cx(
  "outline-none data-focus-visible:outline-2 data-focus-visible:-outline-offset-2 data-focus-visible:outline-solid data-focus-visible:outline-focus",
);
