import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

/** What the console needs before its first render (spec 0014 CPX-3, spec 0016 FRS-1). */
export type ConsoleConfig = components["schemas"]["ConsoleConfigResponse"];

/**
 * The console config, loaded once at boot (spec 0003 RTE-10) and read by the root route's setup redirect
 * (spec 0016 FRS-3). It changes only when setup completes, which refetches it.
 */
export const consoleConfigQuery = () =>
  queryOptions({
    queryKey: ["console-config"],
    queryFn: ({ signal }) => get("/v1/console/config", { signal }),
    staleTime: Number.POSITIVE_INFINITY,
  });
