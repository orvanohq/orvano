import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { DEFAULT_PAGE_SIZE, type ListPage } from "@/shared/url/paging";

export type Project = components["schemas"]["ProjectResponse"];

/** Spec 0017 PJT-5. */
export const projectQuery = (projectId: string) =>
  queryOptions({
    queryKey: ["projects", projectId],
    queryFn: ({ signal }) =>
      get("/v1/console/projects/{projectId}", { params: { path: { projectId } }, signal }),
  });

/** Every cached page of an organization's projects, as a query filter. */
export const projectsFilter = (organizationId: string) => ({
  queryKey: ["organizations", organizationId, "projects"],
});

/** One page of an organization's projects. */
export const projectsQuery = (
  organizationId: string,
  { cursor, limit }: ListPage = { limit: DEFAULT_PAGE_SIZE },
) =>
  queryOptions({
    queryKey: [...projectsFilter(organizationId).queryKey, { cursor, limit }],
    queryFn: ({ signal }) =>
      get("/v1/console/organizations/{organizationId}/projects", {
        params: { path: { organizationId }, query: { cursor, limit } },
        signal,
      }),
  });

/** The projects the switcher shows (spec 0015 SHL-12). */
export async function searchProjects(
  organizationId: string,
  query: string,
  { limit, signal }: { limit: number; signal: AbortSignal },
) {
  const page = await get("/v1/console/organizations/{organizationId}/projects", {
    params: { path: { organizationId }, query: { query: query || undefined, limit } },
    signal,
  });
  return page.items;
}
