import type { ApiKey } from "../api/queries";

/** Spec 0001 UX-33: all that shows of a key after the one time it's revealed. */
export function maskedKey(key: Pick<ApiKey, "prefix" | "last4">): string {
  return `${key.prefix}…${key.last4}`;
}
