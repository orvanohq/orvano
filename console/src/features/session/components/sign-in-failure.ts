import { plural, t } from "@lingui/core/macro";
import { ApiError } from "@/shared/api/errors";

/** Spec 0017 AC-5: what a refused sign in says. It never says which of the two was wrong (PWD-5). */
export function signInFailure(error: unknown): string {
  const kind = error instanceof ApiError ? error.kind : undefined;
  switch (kind) {
    case "unauthenticated":
      return t`The email or password is wrong.`;
    case "rate_limited": {
      const retryAfter = error instanceof ApiError ? error.retryAfter : undefined;
      if (retryAfter === undefined) return t`Too many tries. Wait a moment, then try again.`;
      const minutes = Math.ceil(retryAfter / 60);
      return plural(minutes, {
        one: "Too many tries. Try again in # minute.",
        other: "Too many tries. Try again in # minutes.",
      });
    }
    case "forbidden":
      return t`Open the console at its own address, then try again.`;
    case "network":
    case "timeout":
      return t`Couldn't reach the server. Check your connection and try again.`;
    default:
      return t`The server had a problem. Try again in a moment.`;
  }
}
