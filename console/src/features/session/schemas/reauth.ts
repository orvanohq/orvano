import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type ReauthRequest = components["schemas"]["ReauthRequest"];

/** Spec 0017 SES-8. */
export const reauthSchema = z.object({
  password: z.string().check(z.minLength(1, { error: () => t`Enter your password.` })),
});

export function toReauthRequest(values: z.output<typeof reauthSchema>): ReauthRequest {
  return { password: values.password };
}
