import { QueryClient } from "@tanstack/react-query";
import { isInteractionLocked } from "@/shared/stores/interaction-lock";

// Spec 0003 QRY-4. The read retry rule (network errors and 502 to 504 only) needs ApiError, which arrives with the
// API client (feature 10); until then reads do not retry. Refocus and reconnect skip refetching while the
// interaction lock is held (UX-19).
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        networkMode: "always",
        retry: false,
        refetchOnWindowFocus: () => !isInteractionLocked(),
        refetchOnReconnect: () => !isInteractionLocked(),
      },
      mutations: { networkMode: "always", retry: false },
    },
  });
}
