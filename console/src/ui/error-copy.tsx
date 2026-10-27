import type { ReactNode } from "react";
import { Plural, useLingui } from "@lingui/react/macro";

/**
 * The parts of a failure the error primitives show. Spec 0003 FLT-1's `ApiError` has these fields, so screens pass it
 * as is; any other thrown value reads as a plain failure with no request ID.
 */
export interface ErrorDetails {
  kind?: string;
  requestId?: string;
  requiredRole?: string;
  retryAfter?: number;
}

function text(value: unknown): string | undefined {
  return typeof value === "string" && value !== "" ? value : undefined;
}

export function errorDetails(error: unknown): ErrorDetails {
  if (typeof error !== "object" || error === null) return {};
  const fields = error as Record<string, unknown>;
  return {
    kind: text(fields.kind),
    requestId: text(fields.requestId),
    requiredRole: text(fields.requiredRole),
    retryAfter: typeof fields.retryAfter === "number" ? fields.retryAfter : undefined,
  };
}

/**
 * Spec 0001 UX-22 and design.md _Writing in the UI_: what failed and what to do next, by the error's kind. `title`
 * names what failed ("Couldn't load tables") for a plain failure; the other kinds have their own wording.
 */
export function useErrorCopy(
  error: unknown,
  title?: string,
): { title: string; body: ReactNode; details: ErrorDetails } {
  const { t } = useLingui();
  const details = errorDetails(error);
  const role = details.requiredRole;
  switch (details.kind) {
    case "not_found":
      return {
        details,
        title: t`Not found`,
        body: t`It may have been deleted, or the link is out of date.`,
      };
    case "forbidden":
      return {
        details,
        title: t`You don't have access`,
        body: role
          ? t`You need the ${role} role for this.`
          : t`Ask an owner of this project for access.`,
      };
    case "rate_limited":
      return {
        details,
        title: t`Too many requests`,
        body:
          details.retryAfter === undefined ? (
            t`Wait a moment, then try again.`
          ) : (
            <Plural
              value={Math.ceil(details.retryAfter)}
              one="Try again in # second."
              other="Try again in # seconds."
            />
          ),
      };
    case "network":
    case "timeout":
      return {
        details,
        title: title ?? t`Couldn't reach the server`,
        body: t`Check your connection and try again.`,
      };
    default:
      return {
        details,
        title: title ?? t`Something went wrong`,
        body: details.requestId
          ? t`Try again. If it keeps failing, share the request ID with your administrator.`
          : t`Try again in a moment.`,
      };
  }
}
