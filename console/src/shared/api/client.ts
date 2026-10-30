import createClient, {
  type ClientPathsWithMethod,
  type MaybeOptionalInit,
  type MethodResponse,
} from "openapi-fetch";
import { config } from "@/shared/config";
import { ApiError, apiErrorFromResponse } from "./errors";
import type { paths } from "./schema.gen";

// Spec 0003 QRY-1: the console's one HTTP client, typed by schema.gen.ts (`pnpm api:gen` from api/console.openapi.json).
// Same origin, so the browser sends the console's own credentials and nothing else (SEC-1).
const client = createClient<paths>({ baseUrl: config.apiBaseUrl, credentials: "same-origin" });

type Client = typeof client;
export type GetPath = ClientPathsWithMethod<Client, "get">;
export type PostPath = ClientPathsWithMethod<Client, "post">;

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
  if (!result.response.ok) throw apiErrorFromResponse(result.response, result.error);
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
