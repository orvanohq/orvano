import { queryOptions, type QueryClient } from "@tanstack/react-query";
import { connectCsrfToken, get } from "@/shared/api/client";
import { ApiError } from "@/shared/api/errors";
import type { components } from "@/shared/api/schema.gen";

/** The signed in operator and their session (spec 0017 SES-5). */
export type Session = components["schemas"]["SessionResponse"];

/**
 * Spec 0017 BOT-2, spec 0003 RTE-10: the session, loaded at boot. `null` means signed out, since a 401 here is no
 * failure. It changes through sign in, sign out, and account changes, which write it here or reload the page.
 */
export const sessionQuery = () =>
  queryOptions({
    queryKey: ["session"],
    queryFn: async ({ signal }): Promise<Session | null> => {
      try {
        return await get("/v1/console/session", { signal });
      } catch (error) {
        if (error instanceof ApiError && error.kind === "unauthenticated") return null;
        throw error;
      }
    },
    staleTime: Number.POSITIVE_INFINITY,
  });

/** Spec 0017 SES-7: state changes carry the CSRF token the session query holds. */
export function connectSessionToken(queryClient: QueryClient): void {
  connectCsrfToken(() => queryClient.getQueryData(sessionQuery().queryKey)?.csrfToken);
}
