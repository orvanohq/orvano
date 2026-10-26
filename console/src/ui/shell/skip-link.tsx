import { Trans } from "@lingui/react/macro";

/**
 * Spec 0001 UX-1, spec 0015 SHL-3: the first focusable element, visible only when focused. It moves focus to
 * `main` itself instead of following `#main`, so the router never sees a hash change. `tabIndex` keeps it in the Tab
 * order in Safari, which skips plain links by default (React Aria's links set it too).
 */
export function SkipLink() {
  return (
    <a
      href="#main"
      tabIndex={0}
      onClick={(event) => {
        event.preventDefault();
        document.getElementById("main")?.focus();
      }}
      className="sr-only rounded-md bg-bg-raised px-3 py-2 font-semibold text-accent-text shadow-sm focus:not-sr-only focus:absolute focus:start-2 focus:top-2 focus:z-(--z-tooltip) focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus"
    >
      <Trans>Skip to content</Trans>
    </a>
  );
}
