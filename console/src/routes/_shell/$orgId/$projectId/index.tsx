import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { apiKeysQuery } from "@/features/api-keys";
import { ProjectOverview } from "@/features/projects/components/project-overview";

export const Route = createFileRoute("/_shell/$orgId/$projectId/")({
  staticData: { crumb: msg`Overview` },
  loader: ({ context, params }) => context.queryClient.query(apiKeysQuery(params.projectId)),
  component: ProjectOverview,
});
