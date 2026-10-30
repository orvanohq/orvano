import { QueryClient } from "@tanstack/react-query";
import { isInteractionLocked } from "@/shared/stores/interaction-lock";
import { ApiError } from "./errors";

/** QRY-4: a read retries only when the server was never reached or a gateway said to come back. */
const RETRY_STATUSES = new Set([502, 503, 504]);
const MAX_READ_RETRIES = 2;

export function shouldRetryRead(failureCount: number, error: unknown): boolean {
  if (failureCount >= MAX_READ_RETRIES || !(error instanceof ApiError)) return false;
  return (
    error.kind === "network" || (error.status !== undefined && RETRY_STATUSES.has(error.status))
  );
}

// Spec 0003 QRY-4. Reads retry at most twice, on network errors and 502 to 504 only (never a 429 or a timeout);
// writes never retry. Refocus and reconnect skip refetching while the interaction lock is held (UX-19).
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        networkMode: "always",
        retry: shouldRetryRead,
        refetchOnWindowFocus: () => !isInteractionLocked(),
        refetchOnReconnect: () => !isInteractionLocked(),
      },
      mutations: { networkMode: "always", retry: false },
    },
  });
}
