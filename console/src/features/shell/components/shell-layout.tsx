import { useLingui } from "@lingui/react/macro";
import { useQuery, useSuspenseQuery } from "@tanstack/react-query";
import { Outlet, useMatches, useNavigate, useParams } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config";
import { organizationQuery, searchOrganizations } from "@/features/organizations";
import { projectQuery, searchProjects } from "@/features/projects";
import { SessionPrompts, sessionQuery } from "@/features/session";
import { signOutEverywhere } from "@/shared/session-channel";
import { AppShell, type Crumb, type ShellNavItem, type SwitcherSearch } from "@/ui";
import { useDocumentTitle } from "@/ui/shell/use-document-title";

// Spec 0017 CNS-4: Overview, one item per service the project reports, then Settings.
function navFor(
  orgId: string | undefined,
  projectId: string | undefined,
  services: readonly string[] | undefined,
): readonly ShellNavItem[] {
  if (orgId === undefined) return [{ id: "profile", link: { to: "/account" } }];
  if (projectId === undefined) {
    return [
      { id: "projects", link: { to: "/$orgId", params: { orgId }, exact: true } },
      { id: "org-settings", link: { to: "/$orgId/settings", params: { orgId } } },
    ];
  }
  const params = { orgId, projectId };
  return [
    { id: "overview", link: { to: "/$orgId/$projectId", params, exact: true } },
    ...(services ?? []).map((service) => ({
      id: service as ShellNavItem["id"],
      // A service's route comes with the feature that adds it to `services`.
      link: { to: `/$orgId/$projectId/${service}` as "/$orgId/$projectId", params },
    })),
    { id: "settings", link: { to: "/$orgId/$projectId/settings", params } },
  ];
}

const searchOrgs: SwitcherSearch = async (query, options) => {
  const items = await searchOrganizations(query, options);
  return items.map((item) => ({
    id: item.id,
    name: item.name,
    link: { to: "/$orgId", params: { orgId: item.id } },
  }));
};

/** Spec 0017 CNS-3: the shell's model and crumbs, from the session, the organization and project, and `staticData.crumb`. */
export function ShellLayout() {
  const { i18n } = useLingui();
  const { data: session } = useSuspenseQuery(sessionQuery());
  const { data: consoleConfig } = useSuspenseQuery(consoleConfigQuery());
  const { orgId, projectId } = useParams({ strict: false });
  // The `$orgId` and `$projectId` routes load these, so they're in the cache by now.
  const { data: org } = useQuery({
    ...organizationQuery(orgId ?? ""),
    enabled: orgId !== undefined,
  });
  const { data: project } = useQuery({
    ...projectQuery(projectId ?? ""),
    enabled: projectId !== undefined,
  });
  const matches = useMatches();
  const navigate = useNavigate();

  const pages = matches.flatMap((match) =>
    match.staticData.crumb ? [{ crumb: match.staticData.crumb, match }] : [],
  );
  const crumbs: Crumb[] = [
    ...(org ? [{ kind: "org" } as const] : []),
    ...(project ? [{ kind: "project" } as const] : []),
    ...pages.map(({ crumb, match }, index) => ({
      kind: "page" as const,
      label: i18n._(crumb),
      link: index < pages.length - 1 ? { to: match.fullPath, params: match.params } : undefined,
    })),
  ];
  useDocumentTitle(crumbs, {
    project: project ? { id: project.id, name: project.name } : undefined,
    instanceName: consoleConfig.instanceName,
  });
  if (!session) return null; // `_shell`'s guard sends a signed out visit to sign in

  const searchProjectsInOrg: SwitcherSearch = async (query, options) => {
    if (orgId === undefined) return [];
    const items = await searchProjects(orgId, query, options);
    return items.map((item) => ({
      id: item.id,
      name: item.name,
      link: { to: "/$orgId/$projectId", params: { orgId, projectId: item.id } },
    }));
  };

  return (
    <AppShell
      model={{
        user: {
          id: session.operator.id,
          name: session.operator.name,
          email: session.operator.email,
        },
        instanceName: consoleConfig.instanceName,
        scope: project ? "project" : org ? "org" : "account",
        org: org ? { id: org.id, name: org.name } : undefined,
        project: project ? { id: project.id, name: project.name } : undefined,
        nav: navFor(orgId, projectId, project?.services),
        searchOrgs,
        searchProjects: searchProjectsInOrg,
        orgLinks: {
          all: { to: "/" },
          create: orgId
            ? { to: "/$orgId", params: { orgId }, search: { create: "organization" } }
            : undefined,
        },
        projectLinks: orgId
          ? {
              all: { to: "/$orgId", params: { orgId } },
              create: { to: "/$orgId", params: { orgId }, search: { create: "project" } },
            }
          : undefined,
        onShowAccount: () => {
          void navigate({ to: "/account" });
        },
        onSignOut: () => {
          void signOutEverywhere();
        },
      }}
      crumbs={crumbs}
    >
      <Outlet />
      <SessionPrompts />
    </AppShell>
  );
}
