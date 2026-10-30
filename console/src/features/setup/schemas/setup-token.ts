import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type SetupTokenCheckRequest = components["schemas"]["SetupTokenCheckRequest"];

/** Spec 0016 STP-7: the token, 1 to 100 characters once the spaces around it are trimmed (STP-6). */
export const setupTokenSchema = z.object({
  token: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter the setup token.` }),
      z.maxLength(100, { error: () => t`A setup token is at most 100 characters.` }),
    ),
});

export type SetupTokenValues = z.input<typeof setupTokenSchema>;

/** Spec 0003 FRM-3: the form's checked values as the request the contract describes. */
export function toSetupTokenCheckRequest(
  values: z.output<typeof setupTokenSchema>,
): SetupTokenCheckRequest {
  return { token: values.token };
}
