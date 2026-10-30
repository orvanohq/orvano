import type {
  Crumb,
  ResourceListItem,
  RouteTab,
  ShellModel,
  ShellNavItem,
  ShellScope,
  SwitcherItem,
} from "@/ui";
import type { Scenario } from "./settings";

// Spec 0015 GAL-2: placeholder data in the shapes of SHL-1. Paths follow the gallery's routes, which mirror the
// app's shell paths.

export const user = { id: "usr_ada", name: "Ada Lovelace", email: "ada@example.com" };
export const org = { id: "acme", name: "Acme" };
export const project = { id: "storefront", name: "Storefront" };

const projectServices = [
  "auth",
  "databases",
  "storage",
  "realtime",
  "functions",
  "sites",
  "messaging",
  "usage",
  "logs",
  "settings",
] as const;

const ids = { orgId: org.id, projectId: project.id };

export const projectNav: readonly ShellNavItem[] = [
  { id: "overview", link: { to: "/$orgId/$projectId", params: ids, exact: true } },
  ...projectServices.map((service): ShellNavItem => ({
    id: service,
    link: { to: "/$orgId/$projectId/$service", params: { ...ids, service } },
  })),
];

export const orgNav: readonly ShellNavItem[] = [
  { id: "projects", link: { to: "/$orgId", params: { orgId: org.id }, exact: true } },
  { id: "members", link: { to: "/$orgId/members", params: { orgId: org.id } } },
  { id: "org-settings", link: { to: "/$orgId/settings", params: { orgId: org.id } } },
];

export const accountNav: readonly ShellNavItem[] = [
  { id: "profile", link: { to: "/account/$page", params: { page: "profile" } } },
  { id: "security", link: { to: "/account/$page", params: { page: "security" } } },
  { id: "preferences", link: { to: "/account/$page", params: { page: "preferences" } } },
];

const orgs: SwitcherItem[] = [
  { id: org.id, name: org.name, link: { to: "/$orgId", params: { orgId: org.id } } },
  { id: "globex", name: "Globex", link: { to: "/$orgId", params: { orgId: "globex" } } },
];

// RTL-3: a long Latin name, so right to left shows the switcher keeping its start.
export const longProjectName = "Point of sale integrations for European stores";

const projects: SwitcherItem[] = [
  "Storefront",
  "Billing",
  "Analytics",
  "Mobile app",
  longProjectName,
].map((name) => {
  const id = name.toLowerCase().replaceAll(" ", "-");
  return {
    id,
    name,
    link: { to: "/$orgId/$projectId", params: { orgId: org.id, projectId: id } },
  };
});

/** How long a slow fixture takes: long enough to look at the loading state. */
export const SLOW_MS = 4000;

/**
 * A search that filters a fixture list after a short delay, and honors its abort signal. The scenario makes it slow,
 * fail, or find nothing (GAL-2).
 */
const search =
  (items: SwitcherItem[], scenario: Scenario) =>
  (query: string, options: { limit: number; signal: AbortSignal }) =>
    new Promise<readonly SwitcherItem[]>((resolve, reject) => {
      const timer = setTimeout(
        () => {
          if (scenario === "fail") {
            reject(new Error("Network error"));
            return;
          }
          resolve(
            scenario === "empty"
              ? []
              : items
                  .filter((item) => item.name.toLowerCase().includes(query.toLowerCase()))
                  .slice(0, options.limit),
          );
        },
        scenario === "slow" ? SLOW_MS : 300,
      );
      options.signal.addEventListener("abort", () => {
        clearTimeout(timer);
        reject(options.signal.reason as Error);
      });
    });

const navByScope: Record<ShellScope, readonly ShellNavItem[]> = {
  project: projectNav,
  org: orgNav,
  account: accountNav,
};

export function shellModel(scope: ShellScope, scenario: Scenario = "normal"): ShellModel {
  return {
    user,
    scope,
    org: scope === "account" ? undefined : org,
    project: scope === "project" ? project : undefined,
    nav: navByScope[scope],
    searchOrgs: search(orgs, scenario),
    searchProjects: search(projects, scenario),
    orgLinks: { all: { to: "/" }, create: { to: "/" } },
    projectLinks: { all: { to: "/$orgId", params: { orgId: org.id } }, create: { to: "/" } },
    // Tests listen for this; feature 13 signs out here.
    onSignOut: () => {
      window.dispatchEvent(new Event("gallery:sign-out"));
    },
  };
}

export function crumbs(scope: ShellScope, page: string, resource?: string): Crumb[] {
  if (scope === "account")
    return [
      { kind: "page", label: "Account" },
      { kind: "page", label: page },
    ];
  if (scope === "org") return [{ kind: "org" }, { kind: "page", label: page }];
  if (resource)
    return [
      { kind: "org" },
      { kind: "project" },
      {
        kind: "page",
        label: page,
        link: { to: "/$orgId/$projectId/$service", params: { ...ids, service: "databases" } },
      },
      { kind: "page", label: resource },
    ];
  return [{ kind: "org" }, { kind: "project" }, { kind: "page", label: page }];
}

export const tables: readonly ResourceListItem[] = [
  "orders",
  "users",
  "payments",
  "audit_log_entries_with_a_very_long_name_that_truncates",
].map((name) => ({
  id: name,
  name,
  link: {
    to: "/$orgId/$projectId/databases/$tableId",
    params: { ...ids, tableId: name },
  },
}));

export function tableTabs(
  tableId: string,
  g: (text: string) => string = (text) => text,
): readonly RouteTab[] {
  const params = { ...ids, tableId };
  return [
    {
      id: "rows",
      label: g("Rows"),
      link: { to: "/$orgId/$projectId/databases/$tableId", params, exact: true },
    },
    // SHL-14: six tabs, so a narrow frame shows the row scrolling with a fade at its cut edge.
    ...["Columns", "Indexes", "Permissions", "Triggers", "Settings"].map((name) => ({
      id: name.toLowerCase(),
      label: g(name),
      link: {
        to: "/$orgId/$projectId/databases/$tableId/$tab",
        params: { ...params, tab: name.toLowerCase() },
      },
    })),
  ];
}
