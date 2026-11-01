import type { components } from "./schema.gen";

type Problem = components["schemas"]["Problem"];
export type FieldError = components["schemas"]["FieldError"];

/** Spec 0003 FLT-1: what failed, from the status (spec 0014 KND-2), or from the client for `network` and `timeout`. */
export type ApiErrorKind =
  | "network"
  | "timeout"
  | "unauthenticated"
  | "forbidden"
  | "not_found"
  | "conflict"
  | "validation"
  | "rate_limited"
  | "server";

interface ApiErrorInit {
  kind: ApiErrorKind;
  status?: number;
  code: string;
  message: string;
  fieldErrors?: readonly FieldError[];
  requestId?: string;
  requiredRole?: string;
  retryAfter?: number;
  cause?: unknown;
}

/**
 * Spec 0003 FLT-1: the one error the API client throws. UI code switches on `kind`, and on `code` for a specific case,
 * never on `status` or `message`. It holds what the response said, never what the request sent.
 */
export class ApiError extends Error {
  override readonly name = "ApiError";
  readonly kind: ApiErrorKind;
  /** The HTTP status; none for `network` and a client side `timeout`. */
  readonly status: number | undefined;
  /** The problem's snake_case code (spec 0014 PRB-1), `unknown` when the body is not a problem (KND-3). */
  readonly code: string;
  readonly fieldErrors: readonly FieldError[];
  /** The problem's `traceId`, or the `Orvano-Request-Id` header. */
  readonly requestId: string | undefined;
  readonly requiredRole: string | undefined;
  /** Seconds to wait before trying again, on a 429 or a 503 that knows (PRB-6). */
  readonly retryAfter: number | undefined;

  constructor(init: ApiErrorInit) {
    super(init.message, { cause: init.cause });
    this.kind = init.kind;
    this.status = init.status;
    this.code = init.code;
    this.fieldErrors = init.fieldErrors ?? [];
    this.requestId = init.requestId;
    this.requiredRole = init.requiredRole;
    this.retryAfter = init.retryAfter;
  }

  /** A request that never got an answer (a failed `fetch`). */
  static network(cause: unknown): ApiError {
    return new ApiError({
      kind: "network",
      code: "network",
      message: messageOf(cause, "network"),
      cause,
    });
  }

  /** A request the client gave up on after its time limit (QRY-3). */
  static timeout(cause: unknown): ApiError {
    return new ApiError({
      kind: "timeout",
      code: "timeout",
      message: messageOf(cause, "timeout"),
      cause,
    });
  }
}

/**
 * Spec 0003 QRY-8: the session has ended. `unauthorized` counts too: the first refusal clears the cookie (spec 0017
 * SES-4), so every later request, and every other tab, gets the plain 401.
 */
export function isSessionEnded(error: unknown): boolean {
  return (
    error instanceof ApiError && (error.code === "session_expired" || error.code === "unauthorized")
  );
}

/** The English text of what went wrong, for logs; UI code shows wording by `kind`, never this (FLT-1). */
function messageOf(cause: unknown, fallback: string): string {
  return cause instanceof Error && cause.message !== "" ? cause.message : fallback;
}

/** Spec 0014 KND-2: the kind comes from the status alone, the same table every SDK uses. */
export function kindFromStatus(status: number): ApiErrorKind {
  switch (status) {
    case 400:
    case 413:
    case 415:
    case 422:
      return "validation";
    case 401:
      return "unauthenticated";
    case 403:
      return "forbidden";
    case 404:
      return "not_found";
    case 408:
      return "timeout";
    case 409:
    case 412:
    case 428:
      return "conflict";
    case 429:
      return "rate_limited";
    default:
      return "server";
  }
}

function isProblem(body: unknown): body is Partial<Problem> & { code: string } {
  return (
    typeof body === "object" &&
    body !== null &&
    typeof (body as { code?: unknown }).code === "string"
  );
}

/** `Retry-After` in seconds, or as an HTTP date (spec 0014 STB-4). */
function retryAfterHeader(response: Response): number | undefined {
  const value = response.headers.get("Retry-After")?.trim();
  if (!value) return undefined;
  if (/^\d+$/.test(value)) return Number(value);
  const date = Date.parse(value);
  return Number.isNaN(date) ? undefined : Math.max(0, Math.ceil((date - Date.now()) / 1000));
}

/** Spec 0003 FLT-1: maps a failed response and its parsed body (PRB-1, or anything else per KND-3). */
export function apiErrorFromResponse(response: Response, body: unknown): ApiError {
  const problem = isProblem(body) ? body : undefined;
  return new ApiError({
    kind: kindFromStatus(response.status),
    status: response.status,
    code: problem?.code ?? "unknown",
    message: problem?.detail ?? (response.statusText || String(response.status)),
    fieldErrors: problem?.errors,
    requestId: problem?.traceId ?? response.headers.get("Orvano-Request-Id") ?? undefined,
    requiredRole: problem?.requiredRole,
    retryAfter: problem?.retryAfter ?? retryAfterHeader(response),
  });
}
