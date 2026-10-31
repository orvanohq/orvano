import { t } from "@lingui/core/macro";
import { z } from "@/shared/zod";

/** Spec 0017 AKY-8. */
export const renameKeySchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter a key name.` }),
      z.maxLength(128, { error: () => t`A key name is at most 128 characters.` }),
    ),
});
