import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type CreateProjectRequest = components["schemas"]["CreateProjectRequest"];

/** Spec 0017 PJT-1, PJT-6: a name, and one exact origin per line. The server has the last word on each origin. */
export const createProjectSchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, { error: () => t`Enter a project name.` }),
      z.maxLength(128, { error: () => t`A project name is at most 128 characters.` }),
    ),
  allowedOrigins: z.string(),
});

export type CreateProjectValues = z.input<typeof createProjectSchema>;

export function toCreateProjectRequest(
  values: z.output<typeof createProjectSchema>,
): CreateProjectRequest {
  return {
    name: values.name,
    allowedOrigins: values.allowedOrigins
      .split("\n")
      .map((origin) => origin.trim())
      .filter((origin) => origin !== ""),
  };
}
