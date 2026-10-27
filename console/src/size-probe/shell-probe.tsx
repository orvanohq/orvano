import { AppShell, PageHeader, type Crumb, type ShellModel } from "@/ui";

// Spec 0015 STY-9: the shell probe. `vite build --mode size-probe` swaps the welcome page for this file, so
// `pnpm size` measures the first load with AppShell rendered (AC-3, STY-8) until feature 13's `_shell` routes are
// measured directly. It imports only @/ui (never the gallery, GAL-3), and its labels are placeholder props that
// stay out of the catalog. Feature 13 deletes it.

const model: ShellModel = {
  user: { id: "usr_probe", name: "Probe User", email: "probe@example.com" },
  scope: "project",
  org: { id: "org_probe", name: "Probe Org" },
  project: { id: "prj_probe", name: "Probe Project" },
  nav: [],
  searchOrgs: () => Promise.resolve([]),
  searchProjects: () => Promise.resolve([]),
  onSignOut: () => {},
};

const crumbs: Crumb[] = [{ kind: "org" }, { kind: "project" }, { kind: "page", label: "Overview" }];

export function WelcomePage() {
  return (
    <AppShell model={model} crumbs={crumbs}>
      <PageHeader title="Overview" description="The shell probe for the first load budget." />
    </AppShell>
  );
}
