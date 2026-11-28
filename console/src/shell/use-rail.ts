"use client";

import { usePathname } from "next/navigation";

import {
  instanceNav,
  railFor,
  serviceNavItems,
  type NavItem,
  type RailKind,
} from "./nav";

/** The current path and the rail it gets. The path is known at build time for every exported page. */
export function useRail(): {
  pathname: string;
  kind: RailKind;
  items: readonly NavItem[];
} {
  const pathname = usePathname();
  const kind = railFor(pathname);
  const items =
    kind === "instance"
      ? instanceNav
      : kind === "service"
        ? serviceNavItems
        : [];
  return { pathname, kind, items };
}
