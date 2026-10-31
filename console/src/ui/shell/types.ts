import type { RegisteredRouter } from "@tanstack/react-router";
import type { ReactElement, ReactNode } from "react";
import type { BannerProps } from "../banner";
import type { NavId } from "./nav-registry";

// Spec 0015 SHL-1: the shell takes its data through props only. Feature 13 builds these in the `_shell` route from
// its queries and `staticData.crumb`; the gallery builds them from fixtures. Nothing here imports features or the API.

export type ShellScope = "account" | "org" | "project";

export interface ShellLinkOptions {
  /** Spec 0017 CNS-3: a route the console has. The gallery's own routes go through its `galleryLink`. */
  to: keyof RegisteredRouter["routesByPath"];
  params?: Record<string, string>;
  search?: Record<string, unknown>;
  exact?: boolean;
}

export interface ShellModel {
  user: { id: string; name: string; email: string };
  /** What the install calls itself (spec 0017 BOT-1); "Orvano" when left out. */
  instanceName?: string;
  scope: ShellScope;
  org?: { id: string; name: string }; // set for org and project scope
  project?: { id: string; name: string }; // set for project scope
  nav: readonly ShellNavItem[]; // display order; project scope: only services the project allows (RTE-9)
  searchOrgs: SwitcherSearch;
  searchProjects: SwitcherSearch;
  orgLinks?: { all: ShellLinkOptions; create?: ShellLinkOptions };
  projectLinks?: { all: ShellLinkOptions; create?: ShellLinkOptions };
  onSignOut: () => void;
}

export type SwitcherSearch = (
  query: string,
  options: { limit: number; signal: AbortSignal },
) => Promise<readonly SwitcherItem[]>;

export interface ShellNavItem {
  id: NavId;
  link: ShellLinkOptions;
}

export interface SwitcherItem {
  id: string;
  name: string;
  link: ShellLinkOptions;
}

export type Crumb =
  | { kind: "org" | "project" } // label from ShellModel, renders the switcher
  | { kind: "page"; label: string; link?: ShellLinkOptions }; // translated label; no link on the last crumb

export interface AppShellProps {
  model: ShellModel;
  crumbs: readonly Crumb[];
  resourceList?: ReactNode;
  /**
   * Spec 0001 UX-24: the one `Banner` under the top bar, for state that affects the whole project (offline, limits
   * reached). One at a time: the caller picks which state wins. Omit it when nothing applies.
   */
  banner?: ReactElement<BannerProps>;
  children: ReactNode;
}
