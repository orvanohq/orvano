import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type SetupOwnerRequest = components["schemas"]["SetupOwnerRequest"];

/** Spec 0017 OWN-1, PWD-3: names trimmed; the common password list is the server's to check. */
export const ownerSchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter your name.` }),
      z.maxLength(128, { error: () => t`A name is at most 128 characters.` }),
    ),
  email: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter your email.` }),
      z.maxLength(254, { error: () => t`An email is at most 254 characters.` }),
      z.email({ error: () => t`Enter a valid email address.` }),
    ),
  organizationName: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter an organization name.` }),
      z.maxLength(128, { error: () => t`An organization name is at most 128 characters.` }),
    ),
  password: z.string().check(z.minLength(8, { error: () => t`Use at least 8 characters.` })),
});

export type OwnerValues = z.input<typeof ownerSchema>;

export function toSetupOwnerRequest(
  values: z.output<typeof ownerSchema>,
  token: string,
): SetupOwnerRequest {
  return {
    token,
    name: values.name,
    email: values.email,
    password: values.password,
    organizationName: values.organizationName,
  };
}
