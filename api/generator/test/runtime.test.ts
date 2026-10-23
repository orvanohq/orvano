// The TypeScript runtime templates, run through the rendered fixture snapshots with a stub fetch (spec 0005 SQA-2).
// The SDK repos run the full SQA-7 suite; this proves the templates behave before they are copied there.
import { describe, expect, it } from "vitest";
import { match, substitute } from "./snapshots/ts-client/conformance/dispatch.generated.ts";
import { createClientHandle } from "./snapshots/ts-client/src/runtime/options.ts";
import {
  Health,
  OrvanoError,
  Widgets,
  apiVersion,
} from "./snapshots/ts-client/src/generated/index.ts";
import { kindFromStatus } from "./snapshots/ts-client/src/runtime/errors.ts";
import { buildUrl, checkUrl } from "./snapshots/ts-client/src/runtime/url.ts";
import { createClientHandle as createServerHandle } from "./snapshots/ts-server/src/runtime/options.ts";
import { OrvanoError as ServerOrvanoError } from "./snapshots/ts-server/src/generated/index.ts";

type Reply = Response | (() => Response) | Error;

function stub(...replies: Reply[]): {
  fetch: (request: Request) => Promise<Response>;
  requests: Request[];
} {
  const requests: Request[] = [];
  return {
    requests,
    fetch: (request) => {
      requests.push(request);
      const reply = replies.shift();
      if (reply === undefined) return Promise.reject(new Error("No reply scripted."));
      if (reply instanceof Error) return Promise.reject(reply);
      return Promise.resolve(typeof reply === "function" ? reply() : reply);
    },
  };
}

const json = (status: number, body: unknown, headers: Record<string, string> = {}): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });

const problem = (status: number, code: string, extra: Record<string, unknown> = {}): Response =>
  new Response(
    JSON.stringify({
      type: "about:blank",
      title: "T",
      status,
      detail: `detail ${code}`,
      code,
      traceId: "a".repeat(32),
      ...extra,
    }),
    {
      status,
      headers: { "Content-Type": "application/problem+json" },
    },
  );

/** A fetch that never answers, and rejects once its request is aborted. */
function hang(request: Request): Promise<Response> {
  return new Promise((_, reject) => {
    request.signal.addEventListener("abort", () => {
      reject(new DOMException("aborted", "AbortError"));
    });
  });
}

const health = { status: "ok", apiVersions: [apiVersion] };

function client(
  fetch: (request: Request) => Promise<Response>,
  extra: Record<string, unknown> = {},
) {
  return createClientHandle(
    {
      url: "https://orvano.example.com/base/",
      project: "0b8f0d6e-3c9a-4b51-9f66-1f2e3d4c5b6a",
      fetch,
      ...extra,
    },
    "client/0.0.0-test",
    { "Orvano-Key": "pk_test_fixture" },
  );
}

describe("transport", () => {
  it("sends the version, identity, and key headers, and never follows a redirect", async () => {
    const http = stub(json(200, health));
    await expect(new Health(client(http.fetch)).get()).resolves.toEqual(health);
    const sent = http.requests[0];
    expect(sent?.url).toBe("https://orvano.example.com/base/v1/health");
    expect(sent?.method).toBe("GET");
    expect(sent?.redirect).toBe("manual");
    expect(sent?.headers.get("Orvano-Api-Version")).toBe(apiVersion);
    expect(sent?.headers.get("Orvano-Client")).toBe("client/0.0.0-test");
    expect(sent?.headers.get("Orvano-Key")).toBe("pk_test_fixture");
  });

  it("builds data plane paths, queries, and JSON bodies", async () => {
    const http = stub(json(200, { items: [] }), json(201, { id: "w" }));
    const widgets = new Widgets(client(http.fetch));
    await widgets.list({ limit: 5, tag: ["a b", "c"] });
    await widgets.create({ name: "one", nickname: null });
    expect(http.requests[0]?.url).toBe(
      "https://orvano.example.com/base/v1/projects/0b8f0d6e-3c9a-4b51-9f66-1f2e3d4c5b6a/widgets?limit=5&tag=a%20b&tag=c",
    );
    expect(http.requests[1]?.headers.get("Content-Type")).toBe("application/json");
    await expect(http.requests[1]?.json()).resolves.toEqual({ name: "one", nickname: null });
  });

  it("retries a GET after a 503, then answers", async () => {
    const http = stub(problem(503, "unavailable", { retryAfter: 0 }), json(200, health));
    await expect(new Health(client(http.fetch)).get()).resolves.toEqual(health);
    expect(http.requests).toHaveLength(2);
  });

  it("never retries a POST without an idempotency key", async () => {
    const http = stub(problem(503, "unavailable"), json(201, {}));
    const error: unknown = await new Widgets(client(http.fetch))
      .create({ name: "x" })
      .catch((thrown: unknown) => thrown);
    expect(OrvanoError.is(error) && error.kind).toBe("server");
    expect(http.requests).toHaveLength(1);
  });

  it("turns a problem body into an OrvanoError with field errors", async () => {
    const http = stub(
      problem(400, "validation_failed", {
        errors: [
          { field: "name", location: "body", code: "required", message: "Name is required." },
        ],
      }),
    );
    const error: unknown = await new Widgets(client(http.fetch))
      .create({ name: "" })
      .catch((thrown: unknown) => thrown);
    if (!OrvanoError.is(error)) throw new Error("expected an OrvanoError");
    expect(error.toJSON()).toEqual({
      kind: "validation",
      code: "validation_failed",
      message: "detail validation_failed",
      requestId: "a".repeat(32),
      fieldErrors: [
        { field: "name", location: "body", code: "required", message: "Name is required." },
      ],
      requiredRole: undefined,
      retryAfter: undefined,
    });
  });

  it("reads a body that is not a problem as code unknown, keeping the raw body", async () => {
    const http = stub(
      new Response("<html>bad gateway</html>", { status: 502, statusText: "Bad Gateway" }),
    );
    const error: unknown = await new Health(client(http.fetch, { retry: { maxRetries: 0 } }))
      .get()
      .catch((thrown: unknown) => thrown);
    if (!OrvanoError.is(error)) throw new Error("expected an OrvanoError");
    expect([error.kind, error.code, error.message, error.rawBody]).toEqual([
      "server",
      "unknown",
      "Bad Gateway",
      "<html>bad gateway</html>",
    ]);
  });

  it("turns a redirect into a network error without following it (RTM-6)", async () => {
    const http = stub(
      new Response(null, { status: 302, headers: { Location: "https://elsewhere.example/" } }),
    );
    const error: unknown = await new Health(client(http.fetch, { retry: { maxRetries: 0 } }))
      .get()
      .catch((thrown: unknown) => thrown);
    expect(OrvanoError.is(error) && [error.kind, error.status]).toEqual(["network", undefined]);
    expect(http.requests).toHaveLength(1);
  });

  it("maps a fetch TypeError to network and leaves another error unchanged", async () => {
    const network: unknown = await new Health(
      client(stub(new TypeError("down")).fetch, { retry: { maxRetries: 0 } }),
    )
      .get()
      .catch((thrown: unknown) => thrown);
    expect(OrvanoError.is(network) && network.kind).toBe("network");
    const custom = new RangeError("custom");
    await expect(new Health(client(stub(custom).fetch)).get()).rejects.toBe(custom);
  });

  it("gives kind timeout for its own timeout, and the caller's reason for the caller's abort", async () => {
    const timedOut: unknown = await new Health(
      client(hang, { timeout: 20, retry: { maxRetries: 0 } }),
    )
      .get()
      .catch((thrown: unknown) => thrown);
    expect(OrvanoError.is(timedOut) && timedOut.kind).toBe("timeout");

    const controller = new AbortController();
    const reason = new Error("stop");
    const call = new Health(client(hang)).get({ signal: controller.signal });
    controller.abort(reason);
    await expect(call).rejects.toBe(reason);
  });

  it("refuses credential, idempotency, and identity headers from callers", async () => {
    expect(() => client(stub().fetch, { headers: { "orvano-key": "x" } })).toThrow(TypeError);
    await expect(
      new Health(client(stub().fetch)).get({ headers: { Authorization: "x" } }),
    ).rejects.toThrow(TypeError);
  });

  it("refuses a client from the other package, naming the import (SHP-2)", () => {
    const serverClient = createServerHandle(
      { url: "https://orvano.example.com" },
      "server/0.0.0-test",
      {},
    );
    // @ts-expect-error a @orvano/server client is not a @orvano/client client
    expect(() => new Health(serverClient)).toThrow(
      /Import Health and createClient from @orvano\/client/,
    );
  });

  it("brands errors across two package copies (ERR-T3)", () => {
    expect(
      OrvanoError.is(new ServerOrvanoError({ kind: "server", code: "internal", message: "x" })),
    ).toBe(true);
    expect(OrvanoError.is(new Error("x"))).toBe(false);
  });
});

describe("helpers", () => {
  it("0014 KND-2: derives the kind from the status alone", () => {
    const table: [number, string][] = [
      [400, "validation"],
      [401, "unauthenticated"],
      [403, "forbidden"],
      [404, "not_found"],
      [408, "timeout"],
      [409, "conflict"],
      [412, "conflict"],
      [413, "validation"],
      [415, "validation"],
      [422, "validation"],
      [428, "conflict"],
      [429, "rate_limited"],
      [500, "server"],
      [502, "server"],
      [503, "server"],
      [418, "server"],
    ];
    expect(table.map(([status]) => [status, kindFromStatus(status)])).toEqual(table);
  });

  it("checks the client url (RTM-6)", () => {
    expect(checkUrl("https://a.example/x/", false)).toBe("https://a.example/x");
    expect(checkUrl("http://localhost:8080", false)).toBe("http://localhost:8080");
    expect(checkUrl("http://10.0.0.2", true)).toBe("http://10.0.0.2");
    for (const bad of ["http://a.example", "ftp://a", "https://a.example?x=1", "", 3]) {
      expect(() => checkUrl(bad, false)).toThrow(TypeError);
    }
  });

  it("trims a long run of trailing slashes in linear time", () => {
    const slashes = "/".repeat(200_000);
    const started = performance.now();
    expect(checkUrl(`https://a.example${slashes}`, false)).toBe("https://a.example");
    expect(buildUrl(`https://a.example/x${slashes}`, ["v1"])).toBe("https://a.example/x/v1");
    expect(performance.now() - started).toBeLessThan(500);
  });

  it("builds URLs by the RTM-3 rules", () => {
    expect(
      buildUrl("https://a/", ["v1", "x/y"], { b: undefined, a: true, n: 2, list: ["p", "q"] }),
    ).toBe("https://a/v1/x%2Fy?a=true&n=2&list=p&list=q");
  });
});

describe("0014 CNF-3: the generated matcher", () => {
  it("matches objects by subset and arrays exactly", () => {
    expect(match({ a: 1 }, { a: 1, b: 2 })).toEqual([]);
    expect(match({ a: 1 }, { b: 2 })).toEqual(["$.a: missing"]);
    expect(match([1, 2], [1, 2, 3])).toEqual(["$: expected 2 items, got 3"]);
    expect(match({ a: [{ x: 1 }] }, { a: [{ x: 2 }] })).toEqual(["$.a[0].x: expected 1, got 2"]);
    expect(match(null, null)).toEqual([]);
    expect(match(1, "1")).toEqual(['$: expected 1, got "1"']);
  });

  it("knows $any, $uuid, $timestamp, $regex, and $contains", () => {
    expect(match({ $any: true }, null)).toEqual([]);
    expect(match({ a: { $any: true } }, {})).toEqual(["$.a: missing"]);
    expect(match({ $uuid: true }, "0192f0c1-7b9e-7c43-9a51-3f0e8d2b6a10")).toEqual([]);
    expect(match({ $uuid: true }, "0192F0C1-7B9E-7C43-9A51-3F0E8D2B6A10")).toHaveLength(1);
    expect(match({ $timestamp: true }, "2026-10-22T09:14:03.120558Z")).toEqual([]);
    expect(match({ $timestamp: true }, "yesterday")).toHaveLength(1);
    expect(match({ $regex: "^[0-9a-f]{32}$" }, "a".repeat(32))).toEqual([]);
    expect(match({ $regex: "^x" }, "y")).toHaveLength(1);
    expect(match({ $contains: ["b"] }, ["a", "b"])).toEqual([]);
    expect(match({ $contains: ["c"] }, ["a", "b"])).toHaveLength(1);
  });

  it("substitutes ${name} and refuses an unknown name", () => {
    expect(substitute({ a: ["${v}", "x-${v}"] }, { v: "1" })).toEqual({ a: ["1", "x-1"] });
    expect(() => substitute("${missing}", {})).toThrow(/missing/);
  });
});
