import { queryOptions } from "@tanstack/react-query";
import { getConsoleConfig } from "@/shared/api/console-config";

/**
 * The console config, loaded once at boot (spec 0003 RTE-10) and read by the root route's setup redirect
 * (spec 0016 FRS-3). It changes only when setup completes, which refetches it.
 */
export const consoleConfigQuery = () =>
  queryOptions({
    queryKey: ["console-config"],
    queryFn: ({ signal }) => getConsoleConfig(signal),
    staleTime: Number.POSITIVE_INFINITY,
  });
