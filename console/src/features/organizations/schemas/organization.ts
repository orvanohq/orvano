import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type CreateOrganizationRequest = components["schemas"]["CreateOrganizationRequest"];

/** Spec 0017 ORG-1, ORG-3. */
export const organizationSchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter an organization name.` }),
      z.maxLength(128, { error: () => t`An organization name is at most 128 characters.` }),
    ),
});

export function toOrganizationRequest(
  values: z.output<typeof organizationSchema>,
): CreateOrganizationRequest {
  return { name: values.name };
}
