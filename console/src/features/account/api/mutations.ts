import { useMutation, useQueryClient } from "@tanstack/react-query";
import { sessionQuery, type Session, withReauth } from "@/features/session";
import { patch, post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

type UpdateAccountRequest = components["schemas"]["UpdateAccountRequest"];
type ChangePasswordRequest = components["schemas"]["ChangePasswordRequest"];

/**
 * Spec 0017 ACC-1, spec 0003 QRY-6. The cards share one version, so it's read at the write, and again on the run after a
 * password prompt.
 */
export function useUpdateAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateAccountRequest) =>
      withReauth(() => {
        const version = queryClient.getQueryData(sessionQuery().queryKey)?.operator.version ?? "";
        return patch("/v1/console/account", {
          params: { header: { "If-Match": `"${version}"` } },
          body: request,
        });
      }),
    onSuccess: (operator) => {
      queryClient.setQueryData(
        sessionQuery().queryKey,
        (session) => session && { ...session, operator },
      );
    },
  });
}

/** Spec 0017 ACC-2. A plain call, not a mutation, so a password stays out of the mutation cache (SES-3). */
export async function changePassword(
  request: ChangePasswordRequest,
  signal: AbortSignal,
): Promise<Session> {
  return (await post("/v1/console/account/password", { body: request, signal })) as Session;
}
