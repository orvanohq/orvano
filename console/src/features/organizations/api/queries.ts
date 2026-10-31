import { queryOptions } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";

export type Organization = components["schemas"]["OrganizationResponse"];

/** Spec 0017 ORG-3. */
export const organizationQuery = (organizationId: string) =>
  queryOptions({
    queryKey: ["organizations", organizationId],
    queryFn: ({ signal }) =>
      get("/v1/console/organizations/{organizationId}", {
        params: { path: { organizationId } },
        signal,
      }),
  });

/** The organizations the switcher shows (spec 0017 ORG-2, spec 0015 SHL-12). */
export async function searchOrganizations(
  query: string,
  { limit, signal }: { limit: number; signal: AbortSignal },
) {
  const page = await get("/v1/console/organizations", {
    params: { query: { query: query || undefined, limit } },
    signal,
  });
  return page.items;
}
