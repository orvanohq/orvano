import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { projectsQuery } from "@/features/projects";
import { OrganizationPage } from "@/features/projects/components/organization-page";

export const Route = createFileRoute("/_shell/$orgId/")({
  staticData: { crumb: msg`Projects` },
  validateSearch: (search: Record<string, unknown>): { create?: "project" | "organization" } =>
    search.create === "project" || search.create === "organization"
      ? { create: search.create }
      : {},
  loader: ({ context, params }) => context.queryClient.query(projectsQuery(params.orgId)),
  component: OrganizationPage,
});
