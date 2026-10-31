import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type CreateApiKeyRequest = components["schemas"]["CreateApiKeyRequest"];

/** Spec 0012 SCP-1: what a secret key can hold. The grouped checklist comes with build plan step 16. */
export const SECRET_SCOPES = [
  "databases.read",
  "databases.write",
  "functions.execute",
  "functions.read",
  "functions.write",
  "messaging.read",
  "messaging.write",
  "realtime.subscribe",
  "sites.read",
  "sites.write",
  "storage.read",
  "storage.write",
  "users.read",
  "users.write",
] as const;

/** Spec 0017 AKY-1: a secret key needs a scope, and none starts ticked; a publishable key defaults to every client API. */
export const createKeySchema = z
  .object({
    name: z
      .string()
      .check(
        z.trim(),
        z.minLength(1, { error: () => t`Enter a key name.` }),
        z.maxLength(128, { error: () => t`A key name is at most 128 characters.` }),
      ),
    type: z.enum(["publishable", "secret"]),
    scopes: z.array(z.string()),
  })
  .check(
    z.refine((value) => value.type === "publishable" || value.scopes.length > 0, {
      path: ["scopes"],
      error: () => t`Choose at least one scope.`,
    }),
  );

export type CreateKeyValues = z.input<typeof createKeySchema>;

export function toCreateKeyRequest(values: z.output<typeof createKeySchema>): CreateApiKeyRequest {
  return values.type === "publishable"
    ? { type: values.type, name: values.name, scopes: undefined, expiresInDays: undefined }
    : { type: values.type, name: values.name, scopes: values.scopes, expiresInDays: undefined };
}
