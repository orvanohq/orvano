import { describe, expect, it } from "vitest";
import { ApiError } from "./errors";
import { createQueryClient, shouldRetryRead } from "./query-client";

// Spec 0003 QRY-4: the defaults set once for every query and mutation.
describe("createQueryClient", () => {
  it("keeps query data fresh for 30 seconds and fails offline requests at once", () => {
    const queries = createQueryClient().getDefaultOptions().queries;

    expect(queries?.staleTime).toBe(30_000);
    expect(queries?.networkMode).toBe("always");
  });

  it("never retries a mutation and fails it at once when offline", () => {
    const mutations = createQueryClient().getDefaultOptions().mutations;

    expect(mutations?.retry).toBe(false);
    expect(mutations?.networkMode).toBe("always");
  });

  it("creates a separate cache each time", () => {
    expect(createQueryClient().getQueryCache()).not.toBe(createQueryClient().getQueryCache());
  });

  it.each([
    ["a network failure", ApiError.network(new TypeError("Failed to fetch")), true],
    ["a 502", new ApiError({ kind: "server", status: 502, code: "unknown", message: "" }), true],
    ["a 503", new ApiError({ kind: "server", status: 503, code: "unknown", message: "" }), true],
    ["a 504", new ApiError({ kind: "server", status: 504, code: "unknown", message: "" }), true],
    ["a 500", new ApiError({ kind: "server", status: 500, code: "unknown", message: "" }), false],
    [
      "a 429",
      new ApiError({ kind: "rate_limited", status: 429, code: "rate_limited", message: "" }),
      false,
    ],
    [
      "a 404",
      new ApiError({ kind: "not_found", status: 404, code: "not_found", message: "" }),
      false,
    ],
    ["a timeout", ApiError.timeout(new DOMException("Timed out", "TimeoutError")), false],
    ["an error that isn't an ApiError", new Error("boom"), false],
  ])("retries a read after %s: %s", (_name, error, retries) => {
    expect(shouldRetryRead(0, error)).toBe(retries);
  });

  it("retries a read at most twice", () => {
    const error = ApiError.network(new TypeError("Failed to fetch"));

    expect(shouldRetryRead(1, error)).toBe(true);
    expect(shouldRetryRead(2, error)).toBe(false);
  });
});
