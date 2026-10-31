import { Suspense } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useParams } from "@tanstack/react-router";
import { apiKeysQuery } from "@/features/api-keys";
import { consoleConfigQuery } from "@/features/console-config";
import { ContentSkeleton, CopyField, EmptyState, Link, PageHeader, SettingsCard } from "@/ui";
import { projectQuery } from "../api/queries";
import { ConnectPanel } from "./lazy-connect-panel";

/** Spec 0017 CNS-6: the project id, the API address, the keys, and how to connect. */
export function ProjectOverview() {
  const { t } = useLingui();
  const { orgId, projectId } = useParams({ from: "/_shell/$orgId/$projectId/" });
  const { data: project } = useSuspenseQuery(projectQuery(projectId));
  const { data: config } = useSuspenseQuery(consoleConfigQuery());
  const { data: keys } = useSuspenseQuery(apiKeysQuery(projectId));
  const activeKeys = keys.items.filter((key) => key.status === "active").length;
  const url = config.publicUrl ?? window.location.origin;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={project.name} />
      <SettingsCard title={t`Project details`}>
        <div className="flex flex-col gap-4">
          <CopyField label={t`Project ID`} value={project.id} />
          <CopyField label={t`API address`} value={url} />
        </div>
      </SettingsCard>
      {activeKeys === 0 ? (
        <EmptyState
          kind="first-use"
          title={t`Create an API key`}
          description={t`A key lets your app reach this project.`}
          action={
            <Link to="/$orgId/$projectId/settings/keys" params={{ orgId, projectId }}>
              <Trans>Create API key</Trans>
            </Link>
          }
        />
      ) : (
        <SettingsCard title={t`API keys`}>
          <Link to="/$orgId/$projectId/settings/keys" params={{ orgId, projectId }}>
            <Trans>{activeKeys} active</Trans>
          </Link>
        </SettingsCard>
      )}
      {/* BDL-1: the snippets and their tabs load after the page. */}
      <Suspense fallback={<ContentSkeleton />}>
        <ConnectPanel url={url} projectId={projectId} />
      </Suspense>
    </div>
  );
}
