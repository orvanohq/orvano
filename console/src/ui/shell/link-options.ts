import type { ShellLinkOptions } from "./types";

/**
 * Spreads a `ShellLinkOptions` into `Link` or `router.navigate`. ShellLinkOptions stays a plain string until feature
 * 13 registers the shell routes (SHL-1), so the router's typed options are cast here, once.
 */
export function linkOptions(link: ShellLinkOptions) {
  return {
    to: link.to as "/",
    params: link.params as never,
    search: link.search as never,
  };
}
