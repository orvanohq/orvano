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

/**
 * Spec 0001 UX-5: tabs inside a resource are links styled as tabs, each its own route, with `aria-current="page"`
 * on the active one; not ARIA tabs. They keep focus on navigation (RTE-7), and scroll sideways when they don't fit
 * (UX-3), the browser keeping the focused one in view.
 */
export function RouteTabs({ "aria-label": label, tabs }: RouteTabsProps) {
  return (
    <nav aria-label={label} className="overflow-x-auto border-b">
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
