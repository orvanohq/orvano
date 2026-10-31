import { del, post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import type { Session } from "./queries";

type SignInRequest = components["schemas"]["SignInRequest"];

/**
 * Spec 0017 SES-3. A plain call, not a TanStack mutation: a mutation keeps its variables in the mutation cache, and a
 * password stays out of every cache.
 */
export async function signIn(request: SignInRequest, signal: AbortSignal): Promise<Session> {
  return (await post("/v1/console/session", { body: request, signal })) as Session;
}

/** Spec 0017 SES-6. */
export async function signOut(): Promise<void> {
  await del("/v1/console/session", {});
}
