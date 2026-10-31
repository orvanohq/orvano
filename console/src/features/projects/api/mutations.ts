import { useMutation, useQueryClient } from "@tanstack/react-query";
import { withReauth } from "@/features/session";
import { del, get, patch, post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { projectQuery, projectsQuery, type Project } from "./queries";

type CreateProjectRequest = components["schemas"]["CreateProjectRequest"];
type UpdateProjectRequest = components["schemas"]["UpdateProjectRequest"];

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

/** Spec 0017 PJT-5, spec 0003 QRY-6: sends the version the form started from. */
export function useUpdateProject(project: Project) {
  const queryClient = useQueryClient();
  const projectId = project.id;
  return useMutation({
    scope: { id: projectId },
    mutationFn: (request: UpdateProjectRequest) =>
      patch("/v1/console/projects/{projectId}", {
        params: { path: { projectId }, header: { "If-Match": `"${project.version}"` } },
        body: request,
      }),
    onSuccess: async (saved) => {
      queryClient.setQueryData(projectQuery(projectId).queryKey, saved);
      await queryClient.invalidateQueries(projectsQuery(project.organizationId));
    },
  });
}

/** Spec 0017 PJT-7: answers 202 while the job takes it apart. */
export function useDeleteProject(project: Project) {
  const queryClient = useQueryClient();
  const projectId = project.id;
  return useMutation({
    mutationFn: () =>
      withReauth(() =>
        del("/v1/console/projects/{projectId}", {
          params: { path: { projectId }, header: { "If-Match": `"${project.version}"` } },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries(projectsQuery(project.organizationId)),
  });
}

/** Spec 0014 CPX-2: what goes with the project. A plain read, since only the delete dialog asks, once. */
export function loadProjectDependents(projectId: string, signal: AbortSignal) {
  return get("/v1/console/projects/{projectId}/dependents", {
    params: { path: { projectId } },
    signal,
  });
}
