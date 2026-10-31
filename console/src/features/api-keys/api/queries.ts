import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

export type ApiKey = components["schemas"]["ApiKeyResponse"];

/** Spec 0017 AKY-5: a project's keys, newest first. No answer here ever holds a key. */
export const apiKeysQuery = (projectId: string) =>
  queryOptions({
    queryKey: ["projects", projectId, "keys"],
    queryFn: ({ signal }) =>
      get("/v1/console/projects/{projectId}/keys", { params: { path: { projectId } }, signal }),
  });
