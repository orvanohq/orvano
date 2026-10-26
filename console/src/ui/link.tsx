import type { Ref } from "react";
import { createLink, type LinkComponent, type NavigateOptions } from "@tanstack/react-router";
import { Link as AriaLink, type LinkProps as AriaLinkProps } from "react-aria-components";
import { cx, focusRing, focusRingInset } from "./cx";

interface AriaRouterLinkProps extends Omit<AriaLinkProps, "className" | "style"> {
  /**
   * `inline` links in running text are underlined; `nav` links (menus, crumbs, lists) rely on position. `sidebar`
   * is the primary sidebar's item (SHL-5), current on `accent-subtle`.
   */
  variant?: "inline" | "nav" | "sidebar";
  ref?: Ref<HTMLAnchorElement>;
}

const variants = {
  inline: cx(
    "rounded-sm text-accent-text underline underline-offset-2 data-hovered:decoration-2",
    focusRing,
  ),
  nav: cx("rounded-sm text-inherit no-underline", focusRing),
  sidebar: cx(
    "flex h-control items-center gap-2 rounded-md px-2 font-medium text-fg-muted no-underline",
    "data-hovered:bg-bg-muted data-hovered:text-fg",
    "aria-[current=page]:bg-accent-subtle aria-[current=page]:text-accent-subtle-fg",
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
