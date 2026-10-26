import type { Ref } from "react";
import { createLink, type LinkComponent, type NavigateOptions } from "@tanstack/react-router";
import { Link as AriaLink, type LinkProps as AriaLinkProps } from "react-aria-components";
import { cx, focusRing, focusRingInset } from "./cx";

interface AriaRouterLinkProps extends Omit<AriaLinkProps, "className" | "style"> {
  /**
   * `inline` links in running text are underlined; `nav` links (menus, crumbs, lists) rely on position. `sidebar`
   * is the primary sidebar's item (SHL-5), current on `accent-subtle`; `rail` is the same item in the icon rail.
   * `resource` is a resource list row (UX-2, a density row), and `tab` a `RouteTabs` link (UX-5).
   */
  variant?: "inline" | "nav" | "sidebar" | "rail" | "resource" | "tab";
  ref?: Ref<HTMLAnchorElement>;
}

const variants = {
  inline: cx(
    "rounded-sm text-accent-text underline underline-offset-2 data-hovered:decoration-2",
    focusRing,
  ),
  // UX-32: at least 24 px high, so a crumb or the brand link is a big enough target.
  nav: cx("inline-flex min-h-6 items-center rounded-sm text-inherit no-underline", focusRing),
  sidebar: cx(
    "flex h-control items-center gap-2 rounded-md px-2 font-medium text-fg-muted no-underline",
    "data-hovered:bg-bg-muted data-hovered:text-fg",
    "aria-[current=page]:bg-accent-subtle aria-[current=page]:text-accent-subtle-fg",
    focusRingInset,
  ),
  rail: cx(
    "flex size-control-lg items-center justify-center rounded-md text-fg-muted no-underline",
    "data-hovered:bg-bg-muted data-hovered:text-fg",
    "aria-[current=page]:bg-accent-subtle aria-[current=page]:text-accent-subtle-fg",
    focusRingInset,
  ),
  resource: cx(
    "flex h-(--row-height) min-w-0 flex-1 items-center gap-2 rounded-md px-2 text-fg no-underline",
    "data-hovered:bg-bg-muted",
    "aria-[current=page]:bg-accent-subtle aria-[current=page]:font-medium aria-[current=page]:text-accent-subtle-fg",
    focusRingInset,
  ),
  tab: cx(
    "-mb-px flex h-control-lg shrink-0 items-center border-b-2 border-transparent px-3 font-medium whitespace-nowrap text-fg-muted no-underline",
    "data-hovered:text-fg",
    "aria-[current=page]:border-accent aria-[current=page]:text-fg",
    focusRingInset,
  ),
} as const;

function AriaRouterLink({ variant = "inline", ...props }: AriaRouterLinkProps) {
  return <AriaLink {...props} className={variants[variant]} />;
}

const RouterLink = createLink(AriaRouterLink);

/**
 * Spec 0003 RTE-6: the one router link, made with `createLink` over React Aria's `Link`, so `to`, `params`, and
 * `search` stay typed and the router preloads on hover and focus. Feature code uses this or `useNavigate`.
 *
 * React Aria opens links itself (a keyboard Enter included) through `UiRouterProvider`, so the link's navigation
 * options travel with it as `routerOptions`.
 */
export const Link: LinkComponent<typeof AriaRouterLink> = (props) => {
  const { to, from, params, search, hash, state, replace, resetScroll, hashScrollIntoView, mask } =
    props;
  const routerOptions = {
    to,
    from,
    params,
    search,
    hash,
    state,
    replace,
    resetScroll,
    hashScrollIntoView,
    mask,
  } as NavigateOptions;
  const linkProps = { ...props, routerOptions } as typeof props;
  return <RouterLink preload="intent" {...linkProps} />;
};
