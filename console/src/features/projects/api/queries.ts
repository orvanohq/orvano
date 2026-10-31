import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

export type Project = components["schemas"]["ProjectResponse"];

/** Spec 0017 PJT-5. */
export const projectQuery = (projectId: string) =>
  queryOptions({
    queryKey: ["projects", projectId],
    queryFn: ({ signal }) =>
      get("/v1/console/projects/{projectId}", { params: { path: { projectId } }, signal }),
  });

/** The first page of an organization's projects. */
export const projectsQuery = (organizationId: string) =>
  queryOptions({
    queryKey: ["organizations", organizationId, "projects"],
    queryFn: ({ signal }) =>
      get("/v1/console/organizations/{organizationId}/projects", {
        params: { path: { organizationId } },
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
