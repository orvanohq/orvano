import { useMutation, useQueryClient } from "@tanstack/react-query";
import { post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { apiKeysQuery } from "./queries";

type CreateApiKeyRequest = components["schemas"]["CreateApiKeyRequest"];
export type CreatedApiKey = components["schemas"]["CreatedApiKeyResponse"];

/** Spec 0017 AKY-4, spec 0003 SEC-3: `gcTime: 0`, so the key leaves the mutation cache as soon as the form lets go. */
export function useCreateApiKey(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: async (request: CreateApiKeyRequest) =>
      (await post("/v1/console/projects/{projectId}/keys", {
        params: { path: { projectId } },
        body: request,
      })) as CreatedApiKey,
    onSuccess: () => queryClient.invalidateQueries(apiKeysQuery(projectId)),
  });
}
