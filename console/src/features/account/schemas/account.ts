import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type UpdateAccountRequest = components["schemas"]["UpdateAccountRequest"];
type ChangePasswordRequest = components["schemas"]["ChangePasswordRequest"];

/** What the Language field holds for "use my browser", since a field's value is never null. */
export const BROWSER_LOCALE = "browser";

/** Spec 0017 ACC-1. */
export const profileSchema = z.object({
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
});

export function toProfileRequest(values: z.output<typeof profileSchema>): UpdateAccountRequest {
  return { name: values.name, email: values.email };
}

export function toLocaleRequest(locale: string): UpdateAccountRequest {
  return { locale: locale === BROWSER_LOCALE ? null : locale };
}

/** Spec 0017 ACC-2: the common password list is the server's to check. */
export const passwordSchema = z.object({
  currentPassword: z.string().check(z.minLength(1, { error: () => t`Enter your password.` })),
  newPassword: z.string().check(z.minLength(8, { error: () => t`Use at least 8 characters.` })),
});

export function toPasswordRequest(values: z.output<typeof passwordSchema>): ChangePasswordRequest {
  return { currentPassword: values.currentPassword, newPassword: values.newPassword };
}
