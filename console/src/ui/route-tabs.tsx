import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { useRouterState } from "@tanstack/react-router";
import { Link } from "./link";
import { linkOptions } from "./shell/link-options";
import type { ShellLinkOptions } from "./shell/types";

export interface RouteTab {
  id: string;
  label: string;
  link: ShellLinkOptions;
}

export interface RouteTabsProps {
  /** Names the tab bar ("Table"). */
  "aria-label": string;
  tabs: readonly RouteTab[];
}

/** Which edges have tabs cut off: the inline start, the inline end. */
interface Cut {
  start: boolean;
  end: boolean;
}

// The row's scroll position from the inline start: `scrollLeft` is 0 or negative in right to left.
function cutEdges(row: HTMLElement): Cut {
  const max = row.scrollWidth - row.clientWidth;
  const offset = Math.abs(row.scrollLeft);
  // A pixel of slack: zoom can leave the position a fraction short of the end.
  return { start: offset > 1, end: offset < max - 1 };
}

/**
 * Spec 0001 UX-5: tabs inside a resource are links styled as tabs, each its own route, with `aria-current="page"`
 * on the active one; not ARIA tabs. They keep focus on navigation (RTE-7). Spec 0015 SHL-14: the row never wraps
 * and has no More menu. When the tabs don't fit it scrolls on the inline axis, the current tab is scrolled into view
 * on load and on navigation, and a fade marks each edge where tabs are cut off.
 */
export function RouteTabs({ "aria-label": label, tabs }: RouteTabsProps) {
  const rowRef = useRef<HTMLElement>(null);
  const [cut, setCut] = useState<Cut>({ start: false, end: false });
  const pathname = useRouterState({ select: (state) => state.location.pathname });

  // Once per location, so the user's own scrolling isn't undone by a re-render. Scrolls the row only, never the page:
  // a physical delta works in both directions.
  const scrolledFor = useRef<string | null>(null);
  useLayoutEffect(() => {
    const row = rowRef.current;
    const current = row?.querySelector('[aria-current="page"]');
    if (!row || !current || scrolledFor.current === pathname) return;
    scrolledFor.current = pathname;
    const box = row.getBoundingClientRect();
    const tab = current.getBoundingClientRect();
    if (tab.left < box.left) row.scrollLeft -= box.left - tab.left;
    else if (tab.right > box.right) row.scrollLeft += tab.right - box.right;
  }, [pathname]);

  useEffect(() => {
    const row = rowRef.current;
    if (!row) return undefined;
    const update = () => {
      const next = cutEdges(row);
      setCut((previous) =>
        previous.start === next.start && previous.end === next.end ? previous : next,
      );
    };
    update();
    // The row resizes with the window, the list with the tabs and their labels.
    const observer = new ResizeObserver(update);
    observer.observe(row);
    if (row.firstElementChild) observer.observe(row.firstElementChild);
    row.addEventListener("scroll", update, { passive: true });
    return () => {
      observer.disconnect();
      row.removeEventListener("scroll", update);
    };
  }, []);

  return (
    <nav
      ref={rowRef}
      aria-label={label}
      data-cut-start={cut.start || undefined}
      data-cut-end={cut.end || undefined}
      // The fade is a mask, so it works on any background. Gradients are physical, so right to left mirrors it.
      className="overflow-x-auto border-b [--fade-e:0px] [--fade-s:0px] [mask-image:linear-gradient(to_right,transparent,#000_var(--fade-s),#000_calc(100%_-_var(--fade-e)),transparent)] data-cut-end:[--fade-e:2rem] data-cut-start:[--fade-s:2rem] rtl:[mask-image:linear-gradient(to_left,transparent,#000_var(--fade-s),#000_calc(100%_-_var(--fade-e)),transparent)]"
    >
      <ul className="flex">
        {tabs.map((tab) => (
          <li key={tab.id} className="flex">
            <Link
              variant="tab"
              {...linkOptions(tab.link)}
              activeOptions={{ exact: tab.link.exact ?? false, includeSearch: false }}
              state={{ keepFocus: true } as never}
            >
              {tab.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
