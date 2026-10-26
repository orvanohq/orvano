import { useLingui } from "@lingui/react/macro";
import { Icon } from "../icon";
import { Link } from "../link";
import { navRegistry } from "./nav-registry";
import type { ShellNavItem, ShellScope } from "./types";

export interface PrimarySidebarProps {
  scope: ShellScope;
  nav: readonly ShellNavItem[];
}

/**
 * Spec 0015 SHL-5: the current scope's nav, 240 px on `bg-subtle` with a border at the inline end. The current item
 * (the router's active match, exact for Overview) is `aria-current="page"` on `accent-subtle`. Collapsing to the
 * icon rail and the narrow drawer arrive with milestone 2.
 */
export function PrimarySidebar({ scope, nav }: PrimarySidebarProps) {
  const { t, i18n } = useLingui();
  const labels: Record<ShellScope, string> = {
    project: t`Project`,
    org: t`Organization`,
    account: t`Account`,
  };
  return (
    <nav
      aria-label={labels[scope]}
      className="flex w-sidebar shrink-0 flex-col border-e bg-bg-subtle p-2"
    >
      <ul className="flex flex-col gap-0.5">
        {nav.map((item) => {
          const entry = navRegistry[item.id];
          return (
            <li key={item.id}>
              <Link
                variant="sidebar"
                // ShellLinkOptions stays a plain string until feature 13 registers the shell routes (SHL-1).
                to={item.link.to as "/"}
                params={item.link.params as never}
                search={item.link.search as never}
                activeOptions={{ exact: item.link.exact ?? false, includeSearch: false }}
              >
                <Icon icon={entry.icon} />
                <span className="truncate">{i18n._(entry.label)}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
