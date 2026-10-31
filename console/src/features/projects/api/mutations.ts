import { useMutation, useQueryClient } from "@tanstack/react-query";
import { post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { projectsQuery, type Project } from "./queries";

type CreateProjectRequest = components["schemas"]["CreateProjectRequest"];

/** Spec 0017 PJT-1: answers once the project is active. */
export function useCreateProject(organizationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: CreateProjectRequest) =>
      (await post("/v1/console/organizations/{organizationId}/projects", {
        params: { path: { organizationId } },
        body: request,
      })) as Project,
    onSuccess: () => queryClient.invalidateQueries(projectsQuery(organizationId)),
  });
}
