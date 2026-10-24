import { apiVersion, OrvanoHeaders } from "../generated/contract.js";
import { configOf, type ClientConfig, type OrvanoClient, type RequestOptions } from "./client.js";
import { errorFromResponse, networkError, OrvanoError, timeoutError } from "./errors.js";
import { checkHeaders, checkRetries, checkTimeout } from "./options.js";
import { linkSignals, sleep } from "./signals.js";
import { buildUrl, type QueryValue } from "./url.js";

/**
 * One call, as a generated service method describes it.
 * @internal
 */
export interface RequestSpec {
  readonly operationId: string;
  readonly method: "DELETE" | "GET" | "HEAD" | "OPTIONS" | "PATCH" | "POST" | "PUT";
  /** Path segments after the server URL, unencoded. */
  readonly path: readonly string[];
  readonly query?: Readonly<Record<string, QueryValue | undefined>> | undefined;
  /** The JSON body; `undefined` members are left out. */
  readonly body?: Readonly<Record<string, unknown>> | undefined;
  readonly options?: RequestOptions | undefined;
  /** The `version` to send as `If-Match`, on operations that take it (CNC-4). */
  readonly ifMatch?: string | undefined;
}

type Result =
  | { readonly ok: true; readonly value: unknown }
  | { readonly ok: false; readonly error: OrvanoError };

// Spec 0005 SHP-8, spec 0002 LIB-7.
const retryStatuses: ReadonlySet<number> = new Set([408, 429, 502, 503, 504]);
const retryMethods: ReadonlySet<string> = new Set(["GET", "HEAD", "OPTIONS"]);
const maxRetryAfterSeconds = 30;
const baseDelayMs = 500;

/**
 * Sends a call with the client's retry and timeout rules, and returns the parsed body.
 * @internal
 */
export async function request<T>(client: OrvanoClient, spec: RequestSpec): Promise<T> {
  const config = configOf(client);
  const timeout = checkTimeout(spec.options?.timeout) ?? config.timeout;
  const maxRetries = checkRetries(spec.options?.retry) ?? config.maxRetries;
  const callHeaders = checkHeaders(spec.options?.headers);
  const ifMatch = checkIfMatch(spec.ifMatch);
  const headers =
    ifMatch === undefined ? callHeaders : { ...callHeaders, [OrvanoHeaders.ifMatch]: ifMatch };
  for (let attempt = 0; ; attempt++) {
    // oxlint-disable-next-line no-await-in-loop -- attempts run one after another, each after the last failed (SHP-8)
    const result = await sendOnce(config, spec, timeout, headers);
    if (result.ok) return result.value as T;
    if (!shouldRetry(spec, result.error, attempt, maxRetries)) throw result.error;
    // oxlint-disable-next-line no-await-in-loop -- the backoff wait sits between two attempts (SHP-8)
    await sleep(backoff(attempt, result.error.retryAfter), spec.options?.signal);
  }
}

async function sendOnce(
  config: ClientConfig,
  spec: RequestSpec,
  timeout: number,
  callHeaders: Readonly<Record<string, string>>,
): Promise<Result> {
  const callerSignal = spec.options?.signal;
  const linked = linkSignals(callerSignal, timeout);
  try {
    let response: Response;
    let text: string;
    try {
      response = await config.fetch(buildRequest(config, spec, callHeaders, linked.signal));
      if (isRedirect(response)) {
        await response.body?.cancel();
        return {
          ok: false,
          error: networkError(
            new TypeError("The server answered with a redirect, which the SDK never follows."),
          ),
        };
      }
      text = await response.text();
    } catch (thrown) {
      if (callerSignal?.aborted === true) throw callerSignal.reason; // the caller's own abort (ERR-T2)
      if (linked.timedOut()) return { ok: false, error: timeoutError(thrown) };
      if (thrown instanceof TypeError) return { ok: false, error: networkError(thrown) };
      throw thrown; // a custom fetch's own error, unchanged (SHP-8)
    }
    if (!response.ok) return { ok: false, error: errorFromResponse(response, text) };
    return { ok: true, value: readJson(response, text) };
  } finally {
    linked.dispose();
  }
}

function buildRequest(
  config: ClientConfig,
  spec: RequestSpec,
  callHeaders: Readonly<Record<string, string>>,
  signal: AbortSignal,
): Request {
  const headers = new Headers();
  headers.set("Accept", "application/json");
  headers.set(OrvanoHeaders.orvanoApiVersion, apiVersion);
  for (const [name, value] of Object.entries(config.headers)) headers.set(name, value);
  for (const [name, value] of Object.entries(callHeaders)) headers.set(name, value);
  // Set last, so no caller header can replace them (SHP-7).
  headers.set(OrvanoHeaders.orvanoClient, config.clientName);
  for (const [name, value] of Object.entries(config.credentials)) headers.set(name, value);
  let body: string | null = null;
  if (spec.body !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(spec.body);
  }
  return new Request(buildUrl(config.url, spec.path, spec.query), {
    method: spec.method,
    headers,
    body,
    signal,
    // Deviation RTM-6: workerd refuses "error" (it only has "follow" and "manual"), so every runtime sends "manual"
    // and isRedirect turns the answer into a network error. A redirect is still never followed, so a credential
    // header is never replayed to another origin.
    redirect: "manual",
  });
}

/** The `If-Match` value for a `version`: the strong ETag `"<version>"` (CNC-1, CNC-3). */
function checkIfMatch(version: unknown): string | undefined {
  if (version === undefined) return undefined;
  if (typeof version !== "string" || version === "" || /["\s,]/.test(version))
    throw new TypeError("ifMatch must be a resource's version, as its version field holds it.");
  return `"${version}"`;
}

/** A 3xx, or the opaque answer a browser gives for one under `redirect: "manual"`. */
function isRedirect(response: Response): boolean {
  return response.type === "opaqueredirect" || (response.status >= 300 && response.status < 400);
}

function readJson(response: Response, text: string): unknown {
  if (response.status === 204 || text === "") return undefined;
  try {
    return JSON.parse(text) as unknown;
  } catch (thrown) {
    // SHP-8: a body that isn't JSON is a server error, never retried as a network failure.
    throw new OrvanoError({
      kind: "server",
      status: response.status,
      code: "unknown",
      message: "The response body is not valid JSON.",
      requestId: response.headers.get(OrvanoHeaders.orvanoRequestId) ?? undefined,
      rawBody: text.slice(0, 4096),
      cause: thrown,
    });
  }
}

function shouldRetry(
  spec: RequestSpec,
  error: OrvanoError,
  attempt: number,
  maxRetries: number,
): boolean {
  if (attempt >= maxRetries || !retryMethods.has(spec.method)) return false;
  if (error.status === undefined) return error.kind === "network" || error.kind === "timeout";
  if (!retryStatuses.has(error.status)) return false;
  return error.retryAfter === undefined || error.retryAfter <= maxRetryAfterSeconds;
}

/** Exponential backoff with full jitter from 0.5 s, or the server's `Retry-After` (SHP-8). */
function backoff(attempt: number, retryAfter: number | undefined): number {
  if (retryAfter !== undefined) return retryAfter * 1000;
  return Math.random() * baseDelayMs * 2 ** attempt;
}
