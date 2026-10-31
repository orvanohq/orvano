import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { DEFAULT_PAGE_SIZE, type ListPage } from "@/shared/url/paging";

export type ApiKey = components["schemas"]["ApiKeyResponse"];
export type ApiKeyPage = components["schemas"]["PageOfApiKeyResponse"];

/** Every cached page of a project's keys, as a query filter. */
export const apiKeysFilter = (projectId: string) => ({
  queryKey: ["projects", projectId, "keys"],
});

/** Spec 0017 AKY-5: one page of a project's keys, newest first. No answer here ever holds a key. */
export const apiKeysQuery = (
  projectId: string,
  { cursor, limit }: ListPage = { limit: DEFAULT_PAGE_SIZE },
) =>
  queryOptions({
    queryKey: [...apiKeysFilter(projectId).queryKey, { cursor, limit }],
    queryFn: ({ signal }) =>
      get("/v1/console/projects/{projectId}/keys", {
        params: { path: { projectId }, query: { cursor, limit } },
        signal,
      }),
  });
