// Spec 0014 STB-2, STB-3: the stub keeps runs apart by token, answers in order, records requests, and fails loudly.
import assert from "node:assert/strict";
import { after, before, describe, it } from "node:test";
import { createStubServer } from "../../api/conformance/stub/server.mjs";

/** @type {import("node:http").Server} */
let server;
let base = "";
let clock = 0;

before(async () => {
  server = createStubServer({ ttlMs: 1000, now: () => clock });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(undefined)));
  const address = server.address();
  if (address === null || typeof address === "string") throw new Error("no port");
  base = `http://127.0.0.1:${String(address.port)}`;
});

after(() => {
  server.close();
});

/**
 * @param {string} token
 * @param {string} id
 * @param {unknown[]} stub
 */
async function load(token, id, stub) {
  const response = await fetch(`${base}/__stub/runs/${token}/scenarios/${id}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ stub }),
  });
  assert.equal(response.status, 204);
}

/** @param {string} token */
async function requests(token) {
  const response = await fetch(`${base}/__stub/runs/${token}/requests`);
  return /** @type {{ scenario: string, requests: { method: string, path: string, query: Record<string, string[]>, headers: Record<string, string>, body?: unknown }[] }} */ (
    await response.json()
  );
}

describe("0014 STB-2: the stub server", () => {
  it("answers each run's script in order and records what was sent", async () => {
    await load("run-aaaaaaaa", "health.get.retry_after_503", [
      {
        request: { method: "GET", path: "/v1/health" },
        respond: { status: 503, headers: { "Retry-After": "1" } },
      },
      {
        request: { method: "POST", path: "/v1/thing" },
        respond: { status: 201, body: { ok: true } },
      },
    ]);
    const first = await fetch(`${base}/v1/health?x=1&x=2`, {
      headers: { "Orvano-Stub-Run": "run-aaaaaaaa" },
    });
    assert.equal(first.status, 503);
    assert.equal(first.headers.get("retry-after"), "1");
    const second = await fetch(`${base}/v1/thing`, {
      method: "POST",
      headers: { "Orvano-Stub-Run": "run-aaaaaaaa", "Content-Type": "application/json" },
      body: JSON.stringify({ name: "a" }),
    });
    assert.equal(second.status, 201);
    assert.deepEqual(await second.json(), { ok: true });

    const seen = await requests("run-aaaaaaaa");
    assert.equal(seen.scenario, "health.get.retry_after_503");
    assert.deepEqual(
      seen.requests.map((entry) => [entry.method, entry.path, entry.query, entry.body]),
      [
        ["GET", "/v1/health", { x: ["1", "2"] }, undefined],
        ["POST", "/v1/thing", {}, { name: "a" }],
      ],
    );
  });

  it("keeps two runs apart, so parallel runtimes share one stub", async () => {
    await load("run-bbbbbbbb", "a.b.c", [
      {
        request: { method: "GET", path: "/v1/health" },
        respond: { status: 200, body: { run: "b" } },
      },
    ]);
    await load("run-cccccccc", "a.b.d", [
      {
        request: { method: "GET", path: "/v1/health" },
        respond: { status: 200, body: { run: "c" } },
      },
    ]);
    const c = await fetch(`${base}/v1/health`, { headers: { "Orvano-Stub-Run": "run-cccccccc" } });
    const b = await fetch(`${base}/v1/health`, { headers: { "Orvano-Stub-Run": "run-bbbbbbbb" } });
    assert.deepEqual([await b.json(), await c.json()], [{ run: "b" }, { run: "c" }]);
  });

  it("answers 599 naming the scenario for a request the script does not expect", async () => {
    await load("run-dddddddd", "health.get.ok", [
      { request: { method: "GET", path: "/v1/health" }, respond: { status: 200 } },
    ]);
    const wrong = await fetch(`${base}/v1/other`, {
      headers: { "Orvano-Stub-Run": "run-dddddddd" },
    });
    assert.equal(wrong.status, 599);
    assert.match(
      String(/** @type {{ error: string }} */ (await wrong.json()).error),
      /Scenario health\.get\.ok expected GET \/v1\/health, but got GET \/v1\/other/,
    );
    const untokened = await fetch(`${base}/v1/health`);
    assert.equal(untokened.status, 599);
  });

  it("writes an HTTP date for $httpDateIn, sends raw bodies as is, and waits for delayMs", async () => {
    await load("run-eeeeeeee", "a.b.e", [
      {
        request: { method: "GET", path: "/v1/a" },
        respond: { status: 503, headers: { "Retry-After": { $httpDateIn: 5 } } },
      },
      {
        request: { method: "GET", path: "/v1/b" },
        respond: {
          status: 502,
          headers: { "Content-Type": "text/html" },
          rawBody: "<p>no</p>",
          delayMs: 50,
        },
      },
    ]);
    const dated = await fetch(`${base}/v1/a`, { headers: { "Orvano-Stub-Run": "run-eeeeeeee" } });
    const when = Date.parse(dated.headers.get("retry-after") ?? "");
    assert.ok(when > Date.now() + 3000 && when <= Date.now() + 6000);
    const started = Date.now();
    const raw = await fetch(`${base}/v1/b`, { headers: { "Orvano-Stub-Run": "run-eeeeeeee" } });
    assert.ok(Date.now() - started >= 45);
    assert.equal(await raw.text(), "<p>no</p>");
  });

  it("0014 STB-3: answers CORS for any origin and exposes the headers it sends", async () => {
    const preflight = await fetch(`${base}/v1/health`, {
      method: "OPTIONS",
      headers: {
        Origin: "http://localhost:63315",
        "Access-Control-Request-Method": "GET",
        "Access-Control-Request-Headers": "orvano-key, orvano-stub-run",
      },
    });
    assert.equal(preflight.status, 204);
    assert.equal(preflight.headers.get("access-control-allow-origin"), "http://localhost:63315");
    assert.equal(
      preflight.headers.get("access-control-allow-headers"),
      "orvano-key, orvano-stub-run",
    );

    await load("run-ffffffff", "a.b.f", [
      {
        request: { method: "GET", path: "/v1/health" },
        respond: { status: 200, headers: { "Orvano-Request-Id": "abc" }, body: {} },
      },
    ]);
    const answer = await fetch(`${base}/v1/health`, {
      headers: { Origin: "http://localhost:63315", "Orvano-Stub-Run": "run-ffffffff" },
    });
    assert.match(answer.headers.get("access-control-expose-headers") ?? "", /Orvano-Request-Id/);
  });

  it("drops a run once its time is up", async () => {
    await load("run-gggggggg", "a.b.g", []);
    clock += 1001;
    const gone = await fetch(`${base}/__stub/runs/run-gggggggg/requests`);
    assert.equal(gone.status, 404);
  });

  it("records a header or query named __proto__ as data, not as a prototype", async () => {
    await load("run-hhhhhhhh", "a.b.h", [
      { request: { method: "GET", path: "/v1/health" }, respond: { status: 200 } },
    ]);
    await fetch(`${base}/v1/health?__proto__=x&constructor=y`, {
      headers: [
        ["Orvano-Stub-Run", "run-hhhhhhhh"],
        ["__proto__", "polluted"],
      ],
    });
    const [seen] = (await requests("run-hhhhhhhh")).requests;
    assert.deepEqual(Object.getOwnPropertyDescriptor(seen?.query ?? {}, "__proto__")?.value, ["x"]);
    assert.equal(Object.getPrototypeOf(seen?.query), Object.prototype);
    // Node drops a header named __proto__ before the stub sees it; nothing may reach the prototype either way.
    assert.equal(/** @type {Record<string, unknown>} */ (Object.prototype)["polluted"], undefined);
    assert.equal(Object.getPrototypeOf(seen?.headers), Object.prototype);
  });

  it("answers an oversized body with 413 and no exception text", async () => {
    const response = await fetch(`${base}/__stub/runs/run-iiiiiiii/scenarios/a.b.i`, {
      method: "POST",
      body: "x".repeat(1024 * 1024 + 1),
    });
    assert.equal(response.status, 413);
    assert.deepEqual(await response.json(), { error: "The request body is over 1 MB." });
  });

  it("refuses a bad token", async () => {
    const response = await fetch(`${base}/__stub/runs/short/scenarios/a.b.c`, {
      method: "POST",
      body: JSON.stringify({ stub: [] }),
    });
    assert.equal(response.status, 400);
  });
});
