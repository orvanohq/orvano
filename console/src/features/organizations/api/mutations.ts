import { useMutation, useQueryClient } from "@tanstack/react-query";
import { sessionQuery } from "@/features/session";
import { patch, post } from "@/shared/api/client";
import type { components } from "@/shared/api/schema.gen";
import { organizationQuery, type Organization } from "./queries";

type CreateOrganizationRequest = components["schemas"]["CreateOrganizationRequest"];
type UpdateOrganizationRequest = components["schemas"]["UpdateOrganizationRequest"];

/** Spec 0017 ORG-1. The session lists memberships, so it reads again. */
export function useCreateOrganization() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: CreateOrganizationRequest) =>
      (await post("/v1/console/organizations", { body: request })) as Organization,
    onSuccess: () => queryClient.invalidateQueries(sessionQuery()),
  });
}

/** Spec 0017 ORG-3, spec 0003 QRY-6. */
export function useUpdateOrganization(organization: Organization) {
  const queryClient = useQueryClient();
  const organizationId = organization.id;
  return useMutation({
    mutationFn: (request: UpdateOrganizationRequest) =>
      patch("/v1/console/organizations/{organizationId}", {
        params: { path: { organizationId }, header: { "If-Match": `"${organization.version}"` } },
        body: request,
      }),
    onSuccess: async (saved) => {
      queryClient.setQueryData(organizationQuery(organizationId).queryKey, saved);
      await queryClient.invalidateQueries(sessionQuery());
    },
  });
}
