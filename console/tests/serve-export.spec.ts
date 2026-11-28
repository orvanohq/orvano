import { expect, test } from "@playwright/test";

/**
 * The test server mirrors the host's /console rules (spec 0003, "HTTP surface"). Same order as the host's
 * ConsoleFilesTests. The 503 case (no index.html) is covered by the host tests only, because this server
 * always serves a built export.
 */
const security = {
  "x-content-type-options": "nosniff",
  "referrer-policy": "strict-origin-when-cross-origin",
  "x-frame-options": "DENY",
  "content-security-policy": "frame-ancestors 'none'",
};

test.describe("serve-export matches the host", () => {
  test("other methods answer 405", async ({ request }) => {
    const response = await request.post("/console/");
    expect(response.status()).toBe(405);
    expect(response.headers()["allow"]).toBe("GET, HEAD");
    expect(response.headers()).toMatchObject({
      ...security,
      "cache-control": "no-cache",
    });
  });

  test("/console redirects to /console/ with 308", async ({ request }) => {
    const response = await request.get("/console", { maxRedirects: 0 });
    expect(response.status()).toBe(308);
    expect(response.headers()["location"]).toBe("/console/");
  });

  test("a page without its slash redirects with 301", async ({ request }) => {
    const response = await request.get("/console/members?x=1", {
      maxRedirects: 0,
    });
    expect(response.status()).toBe(301);
    expect(response.headers()["location"]).toBe("/console/members/?x=1");
    expect(response.headers()).toMatchObject({
      ...security,
      "cache-control": "no-cache",
    });
  });

  test("hashed assets are immutable, pages are no-cache", async ({
    request,
  }) => {
    const page = await request.get("/console/");
    expect(page.headers()).toMatchObject({
      ...security,
      "cache-control": "no-cache",
    });
    const asset = (await page.text()).match(
      /\/console\/_next\/static\/[^"]+\.js/,
    )?.[0];
    expect(asset).toBeTruthy();
    const response = await request.get(asset!);
    expect(response.status()).toBe(200);
    expect(response.headers()["cache-control"]).toBe(
      "public, max-age=31536000, immutable",
    );
  });

  test("route data files are served", async ({ request }) => {
    const response = await request.get("/console/members/index.txt");
    expect(response.status()).toBe(200);
    expect(response.headers()["cache-control"]).toBe("no-cache");
  });

  test("unknown paths answer 404 with the not found page", async ({
    request,
  }) => {
    const response = await request.get("/console/nope/");
    expect(response.status()).toBe(404);
    expect(await response.text()).toContain("Page not found");
    expect(response.headers()).toMatchObject({
      ...security,
      "cache-control": "no-cache",
    });
  });
});
