// Spec 0014 STB-1 to STB-5: the conformance stub server. Node 24, no dependencies, run as a node:24-alpine
// container by ../compose.yaml. Many runtimes share it at once, so every script and recording is kept per run token:
//   POST /__stub/runs/{token}/scenarios/{id}   body { "stub": [...] }, the scenario's scripted answers
//   GET  /__stub/runs/{token}/requests         what the SDK sent, in order
//   GET  /__stub/health                        the compose health check
// Every other request carries its token in Orvano-Stub-Run and gets the run's next scripted answer, or 599.
import { createServer } from "node:http";
import { fileURLToPath } from "node:url";

const tokenPattern = /^[A-Za-z0-9_-]{8,64}$/;
const idPattern = /^[a-z][a-z0-9_.]{0,127}$/;
const maxBodyBytes = 1024 * 1024;
const runHeader = "orvano-stub-run";
const unmatchedStatus = 599;

/**
 * @typedef {{ request: { method: string, path: string },
 *   respond: { status: number, headers?: Record<string, string | { $httpDateIn: number }>, body?: unknown,
 *     rawBody?: string, delayMs?: number } }} StubEntry
 * @typedef {{ method: string, path: string, query: Record<string, string[]>, headers: Record<string, string>,
 *   body?: unknown }} RecordedRequest
 * @typedef {{ scenario: string, entries: StubEntry[], next: number, requests: RecordedRequest[], expiresAt: number }} Run
 */

/**
 * Makes the stub's HTTP server (not yet listening).
 * @param {{ ttlMs?: number, now?: () => number }} [options] how long a run is kept (STB-2: 10 minutes), and the clock
 * @returns {import("node:http").Server}
 */
export function createStubServer(options = {}) {
  const ttlMs = options.ttlMs ?? 10 * 60 * 1000;
  const now = options.now ?? Date.now;
  /** @type {Map<string, Run>} */
  const runs = new Map();

  return createServer((request, response) => {
    void handle(request, response).catch((/** @type {unknown} */ error) => {
      // The answer names no exception text; the stub's log keeps it for whoever runs the suite.
      console.error("conformance stub:", error);
      const tooLarge = error instanceof BodyTooLargeError;
      if (!response.headersSent) {
        send(request, response, tooLarge ? 413 : 500, {
          error: tooLarge ? "The request body is over 1 MB." : "The stub failed; see its log.",
        });
      } else response.destroy();
    });
  });

  /**
   * @param {import("node:http").IncomingMessage} request
   * @param {import("node:http").ServerResponse} response
   */
  async function handle(request, response) {
    for (const [token, run] of runs) if (run.expiresAt <= now()) runs.delete(token);

    const url = new URL(request.url ?? "/", "http://stub");
    const method = request.method ?? "GET";
    const body = await readBody(request);

    // STB-3: any origin may call, with any header, so browser mode reaches the stub.
    if (method === "OPTIONS" && request.headers["access-control-request-method"] !== undefined) {
      response.writeHead(204, {
        ...corsHeaders(request),
        "Access-Control-Allow-Methods": "DELETE, GET, HEAD, OPTIONS, PATCH, POST, PUT",
        "Access-Control-Allow-Headers": request.headers["access-control-request-headers"] ?? "*",
        "Access-Control-Max-Age": "600",
      });
      response.end();
      return;
    }

    if (url.pathname === "/__stub/health" && method === "GET") {
      send(request, response, 200, { status: "ok" });
      return;
    }

    const load = /^\/__stub\/runs\/([^/]+)\/scenarios\/([^/]+)$/.exec(url.pathname);
    if (load !== null && method === "POST") {
      const [, token = "", id = ""] = load;
      const script = parseJson(body);
      const entries = /** @type {{ stub?: unknown }} */ (script ?? {}).stub;
      if (!tokenPattern.test(token) || !idPattern.test(id) || !isEntryList(entries)) {
        send(request, response, 400, {
          error: "Send a token of 8 to 64 URL safe characters, a scenario ID, and { stub: [...] }.",
        });
        return;
      }
      runs.set(token, {
        scenario: id,
        entries,
        next: 0,
        requests: [],
        expiresAt: now() + ttlMs,
      });
      send(request, response, 204);
      return;
    }

    const recorded = /^\/__stub\/runs\/([^/]+)\/requests$/.exec(url.pathname);
    if (recorded !== null && method === "GET") {
      const run = runs.get(recorded[1] ?? "");
      if (run === undefined) send(request, response, 404, { error: "No run with this token." });
      else send(request, response, 200, { scenario: run.scenario, requests: run.requests });
      return;
    }

    const token = header(request, runHeader);
    const run = token === undefined ? undefined : runs.get(token);
    if (run === undefined) {
      send(request, response, unmatchedStatus, {
        error: `No scenario is loaded for this request. Send the run token in Orvano-Stub-Run (${method} ${url.pathname}).`,
      });
      return;
    }

    run.requests.push(record(request, method, url, body));
    const entry = run.entries[run.next];
    if (
      entry === undefined ||
      entry.request.method !== method ||
      entry.request.path !== url.pathname
    ) {
      const wanted =
        entry === undefined ? "no more requests" : `${entry.request.method} ${entry.request.path}`;
      send(request, response, unmatchedStatus, {
        error: `Scenario ${run.scenario} expected ${wanted}, but got ${method} ${url.pathname} (request ${String(run.requests.length)}).`,
      });
      return;
    }
    run.next += 1;
    await respond(request, response, entry.respond);
  }
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @param {import("node:http").ServerResponse} response
 * @param {StubEntry["respond"]} answer
 */
async function respond(request, response, answer) {
  if (answer.delayMs !== undefined && answer.delayMs > 0) {
    await new Promise((resolve) => setTimeout(resolve, answer.delayMs));
    if (response.destroyed || request.socket.destroyed) return; // the SDK gave up first (its timeout)
  }
  /** @type {Record<string, string>} */
  const headers = {};
  for (const [name, value] of Object.entries(answer.headers ?? {})) {
    headers[name] =
      typeof value === "string"
        ? value
        : new Date(Date.now() + value.$httpDateIn * 1000).toUTCString();
  }
  let payload = "";
  if (answer.rawBody !== undefined) payload = answer.rawBody;
  else if (answer.body !== undefined) {
    payload = JSON.stringify(answer.body);
    if (!Object.keys(headers).some((name) => name.toLowerCase() === "content-type"))
      headers["Content-Type"] = "application/json";
  }
  writeResponse(request, response, answer.status, headers, payload);
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @param {import("node:http").ServerResponse} response
 * @param {number} status
 * @param {unknown} [body]
 */
function send(request, response, status, body) {
  const payload = body === undefined ? "" : JSON.stringify(body);
  writeResponse(
    request,
    response,
    status,
    body === undefined ? {} : { "Content-Type": "application/json" },
    payload,
  );
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @param {import("node:http").ServerResponse} response
 * @param {number} status
 * @param {Record<string, string>} headers
 * @param {string} payload
 */
function writeResponse(request, response, status, headers, payload) {
  // Expose every header this answer carries, by name (a `*` would not cover a credentialed request).
  const expose = Object.keys(headers).join(", ");
  response.writeHead(status, {
    ...headers,
    ...corsHeaders(request),
    ...(expose === "" ? {} : { "Access-Control-Expose-Headers": expose }),
    "Cache-Control": "no-store",
  });
  response.end(payload);
}

/** @param {import("node:http").IncomingMessage} request */
function corsHeaders(request) {
  const origin = header(request, "origin");
  return origin === undefined
    ? { "Access-Control-Allow-Origin": "*" }
    : { "Access-Control-Allow-Origin": origin, Vary: "Origin" };
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @param {string} name lowercase
 * @returns {string | undefined}
 */
function header(request, name) {
  const value = request.headers[name];
  return Array.isArray(value) ? value.join(", ") : value;
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @param {string} method
 * @param {URL} url
 * @param {string} body
 * @returns {RecordedRequest}
 */
function record(request, method, url, body) {
  // Names come from the request, so they become entries of a Map and then own properties through
  // Object.fromEntries, never assignments that a `__proto__` name could turn into a prototype write.
  /** @type {Map<string, string[]>} */
  const values = new Map();
  for (const [name, value] of url.searchParams)
    values.set(name, [...(values.get(name) ?? []), value]);
  const query = Object.fromEntries(values);
  const headers = Object.fromEntries(
    Object.entries(request.headers).flatMap(([name, value]) =>
      value === undefined ? [] : [[name, Array.isArray(value) ? value.join(", ") : value]],
    ),
  );
  /** @type {RecordedRequest} */
  const entry = { method, path: url.pathname, query, headers };
  if (body !== "")
    entry.body = (header(request, "content-type") ?? "").includes("json")
      ? (parseJson(body) ?? body)
      : body;
  return entry;
}

/** A request body over the stub's limit. */
class BodyTooLargeError extends Error {}

/**
 * @param {unknown} value
 * @returns {value is StubEntry[]}
 */
function isEntryList(value) {
  return Array.isArray(value);
}

/** @param {string} text */
function parseJson(text) {
  try {
    return /** @type {unknown} */ (JSON.parse(text));
  } catch {
    return undefined;
  }
}

/**
 * @param {import("node:http").IncomingMessage} request
 * @returns {Promise<string>}
 */
async function readBody(request) {
  /** @type {Buffer[]} */
  const chunks = [];
  let size = 0;
  for await (const buffer of /** @type {AsyncIterable<Buffer>} */ (request)) {
    size += buffer.length;
    if (size > maxBodyBytes) throw new BodyTooLargeError();
    chunks.push(buffer);
  }
  return Buffer.concat(chunks).toString("utf8");
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const port = Number(process.env["STUB_PORT"] ?? "18081");
  createStubServer().listen(port, "0.0.0.0", () => {
    console.log(`conformance stub listening on ${String(port)}`);
  });
}
