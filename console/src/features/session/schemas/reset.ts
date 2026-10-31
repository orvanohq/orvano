import { t } from "@lingui/core/macro";
import { z } from "@/shared/zod";

/** Spec 0017 RST-3: the common password list is the server's to check. */
export const resetSchema = z.object({
  password: z.string().check(z.minLength(8, { error: () => t`Use at least 8 characters.` })),
});
