import { post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

type SetupTokenCheckRequest = components["schemas"]["SetupTokenCheckRequest"];
type SetupOwnerRequest = components["schemas"]["SetupOwnerRequest"];
type SessionResponse = components["schemas"]["SessionResponse"];

/**
 * Spec 0016 STP-7: checks a setup token without using it up; resolves on `204` and throws an `ApiError` otherwise.
 * A plain call, not a TanStack mutation: a mutation keeps its variables in the mutation cache, and FRS-5 keeps the
 * token out of every cache. The form's submit state is the busy state instead (QRY-6).
 */
export async function checkSetupToken(
  request: SetupTokenCheckRequest,
  signal: AbortSignal,
): Promise<void> {
  await post("/v1/console/setup/token", { body: request, signal });
}

/**
 * Spec 0017 OWN-1: makes the owner and signs them in. A plain call for the same reason: the token and the password stay
 * out of the mutation cache.
 */
export async function createOwner(
  request: SetupOwnerRequest,
  signal: AbortSignal,
): Promise<SessionResponse> {
  return (await post("/v1/console/setup/owner", { body: request, signal })) as SessionResponse;
}
