import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type SignInRequest = components["schemas"]["SignInRequest"];

/** Spec 0017 SES-3: only that both are there, so no rule here says anything about the account. */
export const signInSchema = z.object({
  email: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter your email.` }),
      z.maxLength(254, { error: () => t`An email is at most 254 characters.` }),
    ),
  password: z.string().check(z.minLength(1, { error: () => t`Enter your password.` })),
});

export type SignInValues = z.input<typeof signInSchema>;

/** Spec 0003 FRM-3: the form's checked values as the request the contract describes. */
export function toSignInRequest(values: z.output<typeof signInSchema>): SignInRequest {
  return { email: values.email, password: values.password };
}
