import { useLingui } from "@lingui/react/macro";
import { Outlet, useParams } from "@tanstack/react-router";
import { PageHeader, RouteTabs } from "@/ui";

/** Spec 0017 CNS-1, spec 0001 UX-36: the title and the tabs, each tab its own route. */
export function ProjectSettings() {
  const { t } = useLingui();
  const params = useParams({ from: "/_shell/$orgId/$projectId/settings" });
  return (
    <div className="flex flex-col gap-4">
      <PageHeader title={t`Project settings`} />
      <RouteTabs
        aria-label={t`Project settings`}
        tabs={[
          {
            id: "general",
            label: t`General`,
            link: { to: "/$orgId/$projectId/settings/general", params },
          },
          {
            id: "keys",
            label: t`API keys`,
            link: { to: "/$orgId/$projectId/settings/keys", params },
          },
          {
            id: "origins",
            label: t`Allowed origins`,
            link: { to: "/$orgId/$projectId/settings/origins", params },
          },
        ]}
      />
      <Outlet />
    </div>
  );
}
