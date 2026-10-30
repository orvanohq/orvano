import { HttpResponse, delay } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { http, problem } from "@/test/handlers";
import { worker } from "@/test/msw";
import { get, post } from "./client";
import { ApiError, apiErrorFromResponse, kindFromStatus, type FieldError } from "./errors";

afterEach(() => {
  vi.restoreAllMocks();
});

// Spec 0003 QRY-1 and FLT-1: the one client returns the data or throws an ApiError, mapped from spec 0014 PRB-1.
describe("the API client", () => {
  it("0003 QRY-1: returns a read's data", async () => {
    worker.use(
      http.get("/v1/console/config", ({ response }) => response(200).json({ setupRequired: true })),
    );

    await expect(
      get("/v1/console/config", { signal: new AbortController().signal }),
    ).resolves.toEqual({
      setupRequired: true,
    });
  });

  it("0003 QRY-1: sends a write's body as JSON and resolves a 204 with no data", async () => {
    let sent: unknown;
    worker.use(
      http.post("/v1/console/setup/token", async ({ request, response }) => {
        sent = await request.json();
        return response(204).empty();
      }),
    );

    await expect(
      post("/v1/console/setup/token", { body: { token: "abc" } }),
    ).resolves.toBeUndefined();
    expect(sent).toEqual({ token: "abc" });
  });

  it("0003 FLT-1: maps a problem body to an ApiError", async () => {
    worker.use(
      http.post("/v1/console/setup/token", () =>
        problem(429, "rate_limited", {
          detail: "Slow down.",
          retryAfter: 42,
          headers: { "Retry-After": "42" },
        }),
      ),
    );

    const error = await post("/v1/console/setup/token", { body: { token: "abc" } }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      kind: "rate_limited",
      status: 429,
      code: "rate_limited",
      message: "Slow down.",
      requestId: "4bf92f3577b34da6a3ce929d0e0e4736",
      retryAfter: 42,
    });
  });

  it("0003 FLT-1: maps field errors from a validation problem", async () => {
    const errors: FieldError[] = [
      { field: "token", location: "body", code: "required", message: "Required." },
    ];
    worker.use(
      http.untyped.post("/v1/console/setup/token", () =>
        HttpResponse.json(
          {
            type: "about:blank",
            title: "Bad Request",
            status: 400,
            detail: "Fix it.",
            code: "validation_failed",
            traceId: "t1",
            errors,
          },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );

    const error = await post("/v1/console/setup/token", { body: { token: "" } }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toMatchObject({
      kind: "validation",
      code: "validation_failed",
      fieldErrors: errors,
      requestId: "t1",
    });
  });

  it("0014 KND-3: a body that isn't a problem gives code unknown, with the request ID from the header", async () => {
    worker.use(
      http.untyped.get("/v1/console/config", () =>
        HttpResponse.text("<html>Bad gateway</html>", {
          status: 502,
          statusText: "Bad Gateway",
          headers: { "Orvano-Request-Id": "abc123" },
        }),
      ),
    );

    const error = await get("/v1/console/config", { signal: new AbortController().signal }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toMatchObject({
      kind: "server",
      status: 502,
      code: "unknown",
      message: "Bad Gateway",
      requestId: "abc123",
    });
  });

  it("0003 FLT-1: a failed fetch becomes kind network", async () => {
    worker.use(http.get("/v1/console/config", () => HttpResponse.error()));

    const error = await get("/v1/console/config", { signal: new AbortController().signal }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ kind: "network", status: undefined });
  });

  it("0003 QRY-3: a cancel by the caller is rethrown as it is, never as an ApiError", async () => {
    worker.use(
      http.get("/v1/console/config", async ({ response }) => {
        await delay("infinite");
        return response(200).json({ setupRequired: false });
      }),
    );
    const controller = new AbortController();

    const request = get("/v1/console/config", { signal: controller.signal }).catch(
      (caught: unknown) => caught,
    );
    controller.abort();
    const error = await request;

    expect(error).not.toBeInstanceOf(ApiError);
    expect(error).toHaveProperty("name", "AbortError");
  });

  it("0003 QRY-3: a request past the time limit becomes kind timeout", async () => {
    vi.spyOn(AbortSignal, "timeout").mockReturnValue(
      AbortSignal.abort(new DOMException("The operation timed out.", "TimeoutError")),
    );
    worker.use(
      http.get("/v1/console/config", async ({ response }) => {
        await delay("infinite");
        return response(200).json({ setupRequired: false });
      }),
    );

    const error = await get("/v1/console/config", { signal: new AbortController().signal }).catch(
      (caught: unknown) => caught,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ kind: "timeout", code: "timeout" });
  });
});

// Spec 0014 KND-2: the kind comes from the status alone.
describe("kindFromStatus", () => {
  it.each([
    [400, "validation"],
    [413, "validation"],
    [415, "validation"],
    [422, "validation"],
    [401, "unauthenticated"],
    [403, "forbidden"],
    [404, "not_found"],
    [408, "timeout"],
    [409, "conflict"],
    [412, "conflict"],
    [428, "conflict"],
    [429, "rate_limited"],
    [418, "server"],
    [500, "server"],
    [503, "server"],
  ])("maps %i to %s", (status, kind) => {
    expect(kindFromStatus(status)).toBe(kind);
  });
});

describe("apiErrorFromResponse", () => {
  it("0014 STB-4: reads Retry-After as seconds or as an HTTP date", () => {
    vi.spyOn(Date, "now").mockReturnValue(Date.parse("2026-10-30T12:00:00Z"));
    const asDate = new Response(null, {
      status: 503,
      headers: { "Retry-After": "Fri, 30 Oct 2026 12:00:30 GMT" },
    });
    const asSeconds = new Response(null, { status: 429, headers: { "Retry-After": "7" } });

    expect(apiErrorFromResponse(asDate, undefined).retryAfter).toBe(30);
    expect(apiErrorFromResponse(asSeconds, undefined).retryAfter).toBe(7);
  });
});
