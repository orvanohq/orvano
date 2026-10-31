import type { ShellLinkOptions } from "./types";

/**
 * Spreads a `ShellLinkOptions` into `Link` or `router.navigate`. `to` is a registered route (CNS-3), but its params and
 * search stay loose, so the router's typed options are cast here, once.
 */
export function linkOptions(link: ShellLinkOptions) {
  return {
    to: link.to as "/",
    params: link.params as never,
    search: link.search as never,
  };
}
