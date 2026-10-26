import type { ReactNode } from "react";
import type { NavId } from "./nav-registry";

// Spec 0015 SHL-1: the shell takes its data through props only. Feature 13 builds these in the `_shell` route from
// its queries and `staticData.crumb`; the gallery builds them from fixtures. Nothing here imports features or the API.

export type ShellScope = "account" | "org" | "project";

// Loose until feature 13 registers the shell routes, then narrowed to the router's typed link options.
export interface ShellLinkOptions {
  to: string;
  params?: Record<string, string>;
  search?: Record<string, unknown>;
  exact?: boolean;
}

export interface ShellModel {
  user: { id: string; name: string; email: string };
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
  children: ReactNode;
}
