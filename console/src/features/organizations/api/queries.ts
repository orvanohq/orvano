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
