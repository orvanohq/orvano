import { post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import type { Session } from "./queries";

type SignInRequest = components["schemas"]["SignInRequest"];
type ReauthRequest = components["schemas"]["ReauthRequest"];
type ResetPasswordRequest = components["schemas"]["ResetPasswordRequest"];

/**
 * Spec 0017 SES-3. A plain call, not a TanStack mutation: a mutation keeps its variables in the mutation cache, and a
 * password stays out of every cache.
 */
export async function signIn(request: SignInRequest, signal: AbortSignal): Promise<Session> {
  return (await post("/v1/console/session", { body: request, signal })) as Session;
}

/** Spec 0017 SES-8. A plain call too, for the same reason. */
export async function confirmPassword(request: ReauthRequest, signal: AbortSignal): Promise<void> {
  await post("/v1/console/session/reauth", { body: request, signal });
}

/** Spec 0017 RST-3. A plain call too, for the same reason. */
export async function resetPassword(
  request: ResetPasswordRequest,
  signal: AbortSignal,
): Promise<void> {
  await post("/v1/console/password-resets", { body: request, signal });
}
