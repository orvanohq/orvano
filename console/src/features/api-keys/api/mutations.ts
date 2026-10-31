import { useMutation, useQueryClient } from "@tanstack/react-query";
import { isReauthRequired, withReauth } from "@/features/session";
import { patch, post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { apiKeysQuery, type ApiKey } from "./queries";

type CreateApiKeyRequest = components["schemas"]["CreateApiKeyRequest"];
export type CreatedApiKey = components["schemas"]["CreatedApiKeyResponse"];

export { isReauthRequired };

/** Spec 0017 AKY-4, spec 0003 SEC-3: `gcTime: 0`, so the key leaves the mutation cache as soon as the form lets go. */
export function useCreateApiKey(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: (request: CreateApiKeyRequest) =>
      withReauth(
        async () =>
          (await post("/v1/console/projects/{projectId}/keys", {
            params: { path: { projectId } },
            body: request,
          })) as CreatedApiKey,
      ),
    onSuccess: () => queryClient.invalidateQueries(apiKeysQuery(projectId)),
  });
}

/** Spec 0003 QRY-6: a write to one key patches that key in the cached page. */
function usePatchKey(projectId: string) {
  const queryClient = useQueryClient();
  return (key: ApiKey) => {
    queryClient.setQueryData(
      apiKeysQuery(projectId).queryKey,
      (page) =>
        page && { ...page, items: page.items.map((item) => (item.id === key.id ? key : item)) },
    );
  };
}

/** Spec 0017 AKY-6. */
export function useRevokeApiKey(projectId: string) {
  const patchKey = usePatchKey(projectId);
  return useMutation({
    mutationFn: (keyId: string) =>
      withReauth(
        async () =>
          (await post("/v1/console/projects/{projectId}/keys/{keyId}/revoke", {
            params: { path: { projectId, keyId } },
          })) as ApiKey,
      ),
    onSuccess: patchKey,
  });
}

/** Spec 0017 AKY-7, spec 0003 SEC-3. */
export function useRegenerateApiKey(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    gcTime: 0,
    mutationFn: (keyId: string) =>
      withReauth(
        async () =>
          (await post("/v1/console/projects/{projectId}/keys/{keyId}/regenerate", {
            params: { path: { projectId, keyId } },
          })) as CreatedApiKey,
      ),
    onSuccess: () => queryClient.invalidateQueries(apiKeysQuery(projectId)),
  });
}

/** Spec 0017 AKY-8: no version, since a key's label is last write wins. */
export function useRenameApiKey(projectId: string, keyId: string) {
  const patchKey = usePatchKey(projectId);
  return useMutation({
    mutationFn: (name: string) =>
      patch("/v1/console/projects/{projectId}/keys/{keyId}", {
        params: { path: { projectId, keyId } },
        body: { name },
      }),
    onSuccess: patchKey,
  });
}
