import { describe, expect, it } from "vitest";
import { config } from "./config";

// Spec 0003 WEB-8 and spec 0007 CSL-1: the base path comes from the build's BASE_URL with no trailing slash, ready
// to prefix paths. Vitest browser mode serves from `/`, so here it is empty; the built image serves `/console`.
describe("config", () => {
  it("gives a base path that is empty or an absolute path without a trailing slash", () => {
    expect(config.basePath).toMatch(/^(\/[^/]+(\/[^/]+)*)?$/);
  });

  it("exposes only build facts", () => {
    expect(Object.keys(config).toSorted()).toEqual(["apiBaseUrl", "basePath", "dev"]);
  });

  it("0003 QRY-1: calls the API on the console's own origin", () => {
    expect(config.apiBaseUrl).toBe("");
  });
});
