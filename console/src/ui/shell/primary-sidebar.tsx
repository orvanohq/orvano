import { useLingui } from "@lingui/react/macro";
import { IconButton } from "../button";
import { cx } from "../cx";
import { Icon } from "../icon";
import { CollapseStartIcon, ExpandEndIcon } from "../icons";
import { Link } from "../link";
import { Tooltip } from "../tooltip";
import { VisuallyHidden } from "../visually-hidden";
import { linkOptions } from "./link-options";
import { navRegistry } from "./nav-registry";
import type { ShellNavItem, ShellScope } from "./types";

export interface PrimarySidebarProps {
  scope: ShellScope;
  nav: readonly ShellNavItem[];
  /**
   * `expanded` (240 px) and `rail` (the 56 px icon rail) sit beside the content from 64rem; `drawer` is the full
   * sidebar inside the narrow `Drawer`, with no collapse button.
   */
  mode?: "expanded" | "rail" | "drawer";
  /** Collapses to the rail or expands it again. Required outside the drawer. */
  onCollapsedChange?: (collapsed: boolean) => void;
}

/** The sidebar's accessible name, which says the scope (SHL-3). */
export function useScopeLabel(scope: ShellScope): string {
  const { t } = useLingui();
  const labels: Record<ShellScope, string> = {
    project: t`Project`,
    org: t`Organization`,
    account: t`Account`,
  };
  return labels[scope];
}

/**
 * Spec 0015 SHL-5: the current scope's nav, 240 px on `bg-subtle` with a border at the inline end. The current item
 * (the router's active match, exact for Overview) is `aria-current="page"` on `accent-subtle`. The collapse button
 * sits at the bottom; collapsed, it is the 56 px icon rail whose items keep their names and show them in a tooltip
 * (UX-40).
 */
export function PrimarySidebar({
  scope,
  nav,
  mode = "expanded",
  onCollapsedChange,
}: PrimarySidebarProps) {
  const { t, i18n } = useLingui();
  const label = useScopeLabel(scope);
  const rail = mode === "rail";

  return (
    <nav
      aria-label={label}
      className={cx(
        "flex shrink-0 flex-col gap-2 bg-bg-subtle p-2",
        mode === "drawer" ? "min-h-0 flex-1 overflow-y-auto" : "border-e",
        rail ? "w-rail" : mode === "expanded" && "w-sidebar",
      )}
    >
      <ul className="flex flex-col gap-0.5">
        {nav.map((item) => {
          const entry = navRegistry[item.id];
          const name = i18n._(entry.label);
          const link = (
            <Link
              variant={rail ? "rail" : "sidebar"}
              {...linkOptions(item.link)}
              activeOptions={{ exact: item.link.exact ?? false, includeSearch: false }}
            >
              <Icon icon={entry.icon} />
              {rail ? (
                <VisuallyHidden>{name}</VisuallyHidden>
              ) : (
                <span className="truncate">{name}</span>
              )}
            </Link>
          );
          return (
            <li key={item.id}>
              {rail ? (
                <Tooltip content={name} placement="end">
                  {link}
                </Tooltip>
              ) : (
                link
              )}
            </li>
          );
        })}
      </ul>
      {mode !== "drawer" && onCollapsedChange && (
        <div className={cx("mt-auto flex", rail ? "justify-center" : "justify-end")}>
          <IconButton
            icon={rail ? ExpandEndIcon : CollapseStartIcon}
            flipInRtl
            aria-label={rail ? t`Expand sidebar` : t`Collapse sidebar`}
            onPress={() => {
              onCollapsedChange(!rail);
            }}
          />
        </div>
      )}
    </nav>
  );
}
