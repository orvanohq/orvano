import { t } from "@lingui/core/macro";
import type { components } from "@/shared/api/schema.gen";
import { z } from "@/shared/zod";

type CreateApiKeyRequest = components["schemas"]["CreateApiKeyRequest"];

/** Spec 0012 SCP-1: what a secret key can hold. */
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

/** Spec 0012 SCP-2: what a publishable key can hold, each one a client API switch. */
export const PUBLISHABLE_SCOPES = [
  "client.auth",
  "client.auth.signup",
  "client.databases",
  "client.functions",
  "client.realtime",
  "client.storage",
] as const;

export type Scope = (typeof SECRET_SCOPES)[number] | (typeof PUBLISHABLE_SCOPES)[number];

/** Spec 0017 AKY-4: a secret key starts with none ticked, and a publishable key with all of them. */
export function defaultScopes(type: "publishable" | "secret"): string[] {
  return type === "secret" ? [] : [...PUBLISHABLE_SCOPES];
}

/** Spec 0012 SCP-2: `client.auth.signup` goes with `client.auth`. */
export function withScope(scopes: readonly string[], scope: string, isOn: boolean): string[] {
  if (isOn) return scopes.includes(scope) ? [...scopes] : [...scopes, scope];
  return scopes.filter(
    (item) => item !== scope && !(scope === "client.auth" && item === "client.auth.signup"),
  );
}

/** Spec 0017 AKY-4: the expiry the form offers. */
export const EXPIRIES = ["never", "30", "90", "365"] as const;

/** Spec 0017 AKY-1: a key needs a scope, of either type: an empty list is refused. */
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
    expiry: z.enum(EXPIRIES),
  })
  .check(
    z.refine((value) => value.scopes.length > 0, {
      path: ["scopes"],
      error: () => t`Choose at least one scope.`,
    }),
  );

export type CreateKeyValues = z.input<typeof createKeySchema>;

export function toCreateKeyRequest(values: z.output<typeof createKeySchema>): CreateApiKeyRequest {
  return {
    type: values.type,
    name: values.name,
    scopes: values.scopes,
    expiresInDays: values.expiry === "never" ? undefined : Number(values.expiry),
  };
}
