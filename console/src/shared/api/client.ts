import createClient, {
  type ClientPathsWithMethod,
  type MaybeOptionalInit,
  type MethodResponse,
} from "openapi-fetch";
import { config } from "@/shared/config";
import { sessionEnded } from "@/shared/stores/session";
import { ApiError, apiErrorFromResponse, isSessionEnded } from "./errors";
import type { paths } from "./schema.gen";

// Spec 0003 QRY-1: the console's one HTTP client, typed by schema.gen.ts (`pnpm api:gen` from api/console.openapi.json).
// Same origin, so the browser sends the console's own credentials and nothing else (SEC-1).
const client = createClient<paths>({ baseUrl: config.apiBaseUrl, credentials: "same-origin" });

/** Where the CSRF token comes from: the session query (spec 0017 SES-7), which the session feature connects at boot. */
let readCsrfToken: () => string | undefined = () => undefined;

export function connectCsrfToken(read: () => string | undefined): void {
  readCsrfToken = read;
}

// eslint-disable-next-line lingui/no-unlocalized-strings -- HTTP methods, never shown
const READ_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

// Spec 0003 SEC-1, spec 0017 SES-7: the one place the header is added, on every state change once there's a session.
client.use({
  onRequest({ request }) {
    const token = readCsrfToken();
    if (token && !READ_METHODS.has(request.method)) {
      // eslint-disable-next-line lingui/no-unlocalized-strings -- a header name, never shown
      request.headers.set("Orvano-Csrf", token);
    }
    return request;
  },
});

type Client = typeof client;
export type GetPath = ClientPathsWithMethod<Client, "get">;
export type PostPath = ClientPathsWithMethod<Client, "post">;
export type PatchPath = ClientPathsWithMethod<Client, "patch">;
export type DeletePath = ClientPathsWithMethod<Client, "delete">;

/** QRY-3: every request gives up after 30 seconds, as an `ApiError` with kind `timeout`. */
export const REQUEST_TIMEOUT_MS = 30_000;

interface Result {
  data?: unknown;
  error?: unknown;
  response: Response;
}

/**
 * Runs one request with the caller's signal and the QRY-3 timeout, and returns its data or throws an `ApiError`
 * (FLT-1). A cancel by the caller is rethrown unchanged, so TanStack Query treats it as a cancel and nothing shows it.
 */
async function send(
  call: (init: { signal: AbortSignal }) => Promise<Result>,
  signal: AbortSignal | undefined,
): Promise<unknown> {
  const timeout = AbortSignal.timeout(REQUEST_TIMEOUT_MS);
  let result: Result;
  try {
    result = await call({ signal: signal ? AbortSignal.any([signal, timeout]) : timeout });
  } catch (error) {
    if (signal?.aborted) throw error;
    throw timeout.aborted ? ApiError.timeout(error) : ApiError.network(error);
  }
  if (!result.response.ok) {
    const error = apiErrorFromResponse(result.response, result.error);
    // QRY-8: only with a session to lose. At boot, a 401 just means signed out (RTE-10).
    if (readCsrfToken() && isSessionEnded(error)) sessionEnded.set(true);
    throw error;
  }
  return result.data;
}

/** A read. `signal` is required, so a query always passes the one TanStack Query gives it (QRY-3). */
export async function get<Path extends GetPath>(
  path: Path,
  options: MaybeOptionalInit<paths[Path], "get"> & { signal: AbortSignal },
): Promise<MethodResponse<Client, "get", Path>> {
  return (await send(
    (init) => client.GET(path, { ...options, ...init } as never),
    options.signal,
  )) as MethodResponse<Client, "get", Path>;
}

/** A write. Resolves with the response's data, or `undefined` for a `204`. */
export async function post<Path extends PostPath>(
  path: Path,
  options: MaybeOptionalInit<paths[Path], "post"> & { signal?: AbortSignal },
): Promise<MethodResponse<Client, "post", Path> | undefined> {
  return (await send(
    (init) => client.POST(path, { ...options, ...init } as never),
    options.signal,
  )) as MethodResponse<Client, "post", Path> | undefined;
}

/** A partial update (spec 0014 JSW-9). */
export async function patch<Path extends PatchPath>(
  path: Path,
  options: MaybeOptionalInit<paths[Path], "patch"> & { signal?: AbortSignal },
): Promise<MethodResponse<Client, "patch", Path>> {
  return (await send(
    (init) => client.PATCH(path, { ...options, ...init } as never),
    options.signal,
  )) as MethodResponse<Client, "patch", Path>;
}

/** A delete. Resolves with the response's data, or `undefined` for a `204`. */
export async function del<Path extends DeletePath>(
  path: Path,
  options: MaybeOptionalInit<paths[Path], "delete"> & { signal?: AbortSignal },
): Promise<MethodResponse<Client, "delete", Path> | undefined> {
  return (await send(
    (init) => client.DELETE(path, { ...options, ...init } as never),
    options.signal,
  )) as MethodResponse<Client, "delete", Path> | undefined;
}
