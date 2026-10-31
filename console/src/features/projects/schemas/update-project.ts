import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";
import { originList } from "./create-project";

type UpdateProjectRequest = components["schemas"]["UpdateProjectRequest"];

/** Spec 0017 PJT-5. */
export const renameProjectSchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter a project name.` }),
      z.maxLength(128, { error: () => t`A project name is at most 128 characters.` }),
    ),
});

export function toRenameProjectRequest(
  values: z.output<typeof renameProjectSchema>,
): UpdateProjectRequest {
  return { name: values.name };
}

/** Spec 0017 PJT-6: one exact origin per line. The server has the last word on each. */
export const originsSchema = z.object({ allowedOrigins: z.string() });

export function toOriginsRequest(values: z.output<typeof originsSchema>): UpdateProjectRequest {
  return { allowedOrigins: originList(values.allowedOrigins) };
}
