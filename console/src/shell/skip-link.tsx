/** The first stop in the tab order. Hidden until focused; moves focus to `main#main`. */
export function SkipLink() {
  return (
    <a
      href="#main"
      className="sr-only rounded-md bg-surface px-3 py-2 text-sm font-medium text-ink shadow-popover focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:ring-2 focus:ring-ring focus:outline-none"
    >
      Skip to main content
    </a>
  );
}
