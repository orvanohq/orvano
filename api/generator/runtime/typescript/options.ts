import { OrvanoHeaders } from "../generated/contract.js";
import { registerClient, type OrvanoClient } from "./client.js";
import { checkUrl } from "./url.js";

/**
 * The `createClient` options both packages share (spec 0005 SHP-1).
 * @public
 */
export interface CommonClientOptions {
  /** The server URL, `https:` (or `http:` for localhost). */
  readonly url: string;
  /** The project ID the data plane calls go to. */
  readonly project?: string | undefined;
  /** Replaces `fetch`, for logging, tracing, a proxy, or tests. It gets a `Request` and returns a `Response`. */
  readonly fetch?: ((request: Request) => Promise<Response>) | undefined;
  /** The per attempt timeout in milliseconds. Default 30000. */
  readonly timeout?: number | undefined;
  /** Retries for idempotent calls. Default `{ maxRetries: 2 }`; `{ maxRetries: 0 }` turns them off. */
  readonly retry?: { readonly maxRetries: number } | undefined;
  /** Headers added to every request. Credential, idempotency, and SDK identity headers can't be set here. */
  readonly headers?: Readonly<Record<string, string>> | undefined;
  /** Allows `http:` for any host, for testing on your own network. */
  readonly allowHttp?: boolean | undefined;
}

const defaultTimeout = 30_000;
const defaultRetries = 2;

// SHP-7: headers callers may never set through `headers`.
const reserved: ReadonlySet<string> = new Set(
  [
    OrvanoHeaders.authorization,
    OrvanoHeaders.orvanoKey,
    OrvanoHeaders.idempotencyKey,
    OrvanoHeaders.orvanoClient,
  ].map((name) => name.toLowerCase()),
);

/**
 * Validates the shared options, throwing `TypeError` at once for a bad value, and makes the client handle.
 * The package's own `createClient` passes its identity and credential headers.
 * @internal
 */
export function createClientHandle(
  options: CommonClientOptions,
  clientName: string,
  credentials: Readonly<Record<string, string>>,
): OrvanoClient {
  if (typeof options !== "object" || options === null)
    throw new TypeError("createClient needs an options object.");
  const url = checkUrl(options.url, options.allowHttp === true);
  if (
    options.project !== undefined &&
    (typeof options.project !== "string" || options.project === "")
  ) {
    throw new TypeError("project must be a non empty string.");
  }
  if (options.fetch !== undefined && typeof options.fetch !== "function")
    throw new TypeError("fetch must be a function.");
  const timeout = checkTimeout(options.timeout) ?? defaultTimeout;
  const maxRetries = checkRetries(options.retry) ?? defaultRetries;
  const customFetch = options.fetch;
  return registerClient({
    url,
    project: options.project,
    clientName,
    credentials,
    headers: checkHeaders(options.headers),
    // RTM-6: looked up at call time, so a fetch patched later (Next.js, a test) is the one used.
    fetch: customFetch ?? ((request) => globalThis.fetch(request)),
    timeout,
    maxRetries,
  });
}

/** @internal */
export function checkTimeout(timeout: unknown): number | undefined {
  if (timeout === undefined) return undefined;
  if (typeof timeout !== "number" || !Number.isFinite(timeout) || timeout <= 0) {
    throw new TypeError("timeout must be a positive number of milliseconds.");
  }
  return timeout;
}

/** @internal */
export function checkRetries(retry: unknown): number | undefined {
  if (retry === undefined) return undefined;
  const maxRetries =
    typeof retry === "object" && retry !== null
      ? (retry as { maxRetries?: unknown }).maxRetries
      : undefined;
  if (
    typeof maxRetries !== "number" ||
    !Number.isInteger(maxRetries) ||
    maxRetries < 0 ||
    maxRetries > 2
  ) {
    throw new TypeError("retry.maxRetries must be 0, 1, or 2.");
  }
  return maxRetries;
}

/** @internal */
export function checkHeaders(headers: unknown): Readonly<Record<string, string>> {
  if (headers === undefined) return {};
  if (typeof headers !== "object" || headers === null)
    throw new TypeError("headers must be an object of strings.");
  const result: Record<string, string> = {};
  for (const [name, value] of Object.entries(headers)) {
    if (typeof value !== "string") throw new TypeError(`The header ${name} must be a string.`);
    if (reserved.has(name.toLowerCase())) {
      throw new TypeError(`The header ${name} is set by the SDK and can't be set through headers.`);
    }
    result[name] = value;
  }
  return result;
}
