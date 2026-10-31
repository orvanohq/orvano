import type { ReactNode } from "react";
import { Plural, useLingui } from "@lingui/react/macro";

/**
 * The parts of a failure the error primitives show. Spec 0003 FLT-1's `ApiError` has these fields, so screens pass it
 * as is; any other thrown value reads as a plain failure with no request ID.
 */
export interface ErrorDetails {
  kind?: string;
  code?: string;
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
    code: text(fields.code),
    requestId: text(fields.requestId),
    requiredRole: text(fields.requiredRole),
    retryAfter: typeof fields.retryAfter === "number" ? fields.retryAfter : undefined,
  };
}

export interface ErrorCopy {
  title: string;
  body: ReactNode;
  details: ErrorDetails;
  /** Whether trying the same request again can help, so a Retry button belongs next to the wording. */
  retryable: boolean;
}

/**
 * Spec 0001 UX-22 and design.md _Writing in the UI_: what failed and what to do next, by the error's kind (spec 0003
 * FLT-1). `title` names what failed ("Couldn't load tables") for a failure the caller can name: a network failure, a
 * server error, or an unknown one; the other kinds have their own wording.
 */
export function useErrorCopy(error: unknown, title?: string): ErrorCopy {
  const { t } = useLingui();
  const details = errorDetails(error);
  const role = details.requiredRole;
  if (details.code === "reauth_required") {
    return {
      details,
      retryable: false,
      title: t`Confirm your password`,
      body: t`This needs your password. Try again and enter it.`,
    };
  }
  switch (details.kind) {
    case "not_found":
      return {
        details,
        retryable: false,
        title: t`Not found`,
        body: t`It may have been deleted, or the link is out of date.`,
      };
    case "unauthenticated":
      return {
        details,
        retryable: false,
        title: t`You're signed out`,
        body: t`Your session ended. Sign in again to continue.`,
      };
    case "forbidden":
      return {
        details,
        retryable: false,
        title: t`You don't have access`,
        body: role
          ? t`You need the ${role} role for this.`
          : t`Ask an owner of this project for access.`,
      };
    case "conflict":
      return {
        details,
        retryable: false,
        title: t`It changed while you were here`,
        body: t`Someone else changed it. Reload the page to see the latest version.`,
      };
    case "validation":
      return {
        details,
        retryable: false,
        title: title ?? t`The request wasn't valid`,
        body: details.requestId
          ? t`Trying again won't help. If you didn't expect this, share the request ID with your administrator.`
          : t`Trying again won't help. Check what you entered and change it.`,
      };
    case "rate_limited":
      return {
        details,
        retryable: true,
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
        retryable: true,
        title: title ?? t`Couldn't reach the server`,
        body: t`Check your connection and try again.`,
      };
    case "server":
      return {
        details,
        retryable: true,
        title: title ?? t`The server had a problem`,
        body: details.requestId
          ? t`Try again. If it keeps failing, share the request ID with your administrator.`
          : t`Try again in a moment.`,
      };
    default:
      return {
        details,
        retryable: true,
        title: title ?? t`Something went wrong`,
        body: details.requestId
          ? t`Try again. If it keeps failing, share the request ID with your administrator.`
          : t`Try again in a moment.`,
      };
  }
}
