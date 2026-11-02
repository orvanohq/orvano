import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { projectsFilter, projectsQuery } from "@/features/projects";
import { OrganizationPage } from "@/features/projects/components/organization-page";
import { ListPageError } from "@/features/shell/components/list-page-error";
import { isPageTurn } from "@/shared/api/placeholder";
import { toListPage, validatePageSearch, type PageSearch } from "@/shared/url/paging";

export const Route = createFileRoute("/_shell/$orgId/")({
  staticData: { crumb: msg`Projects` },
  validateSearch: (
    search: Record<string, unknown>,
  ): PageSearch & { create?: "project" | "organization" | undefined } => ({
    ...validatePageSearch(search),
    create:
      search.create === "project" || search.create === "organization" ? search.create : undefined,
  }),
  loaderDeps: ({ search }) => toListPage(search),
  loader: async ({ cause, context, params, deps }) => {
    if (!isPageTurn(cause, context.queryClient, projectsFilter(params.orgId))) {
      await context.queryClient.query(projectsQuery(params.orgId, deps));
    }
  },
  component: OrganizationPage,
  errorComponent: ListPageError,
});
