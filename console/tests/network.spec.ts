import { expect, test } from "@playwright/test";

import { hydrated, path, projectPath } from "./helpers";

/** The console asks for nothing outside /console: no font host, no analytics, no API call (spec 0003, AC-7). */
for (const url of [
  path("/"),
  projectPath("/project/database/"),
  path("/kit/"),
]) {
  test(`${url} requests only /console files`, async ({ page }) => {
    const requests: string[] = [];
    page.on("request", (request) => requests.push(request.url()));
    await page.goto(url);
    await hydrated(page);
    expect(requests.length).toBeGreaterThan(0);
    expect(
      requests.filter((r) => !r.startsWith("http://localhost:3100/console/")),
    ).toEqual([]);
  });
}

test("the fonts load from /console in weights 400, 500 and 600", async ({
  page,
}) => {
  const fontFiles: string[] = [];
  page.on("response", (response) => {
    if (response.url().endsWith(".woff2")) {
      fontFiles.push(response.url());
    }
  });
  await page.goto(path("/kit/"));
  await hydrated(page);
  const loaded = await page.evaluate(async () => {
    const counts: number[] = [];
    for (const family of ["IBM Plex Sans", "IBM Plex Mono"]) {
      for (const weight of [400, 500, 600]) {
        counts.push(
          (await document.fonts.load(`${weight} 14px "${family}"`)).length,
        );
      }
    }
    return counts;
  });
  expect(loaded).toEqual([1, 1, 1, 1, 1, 1]);
  expect(fontFiles.length).toBeGreaterThanOrEqual(6);
  expect(
    fontFiles.every((url) =>
      url.startsWith("http://localhost:3100/console/_next/static/"),
    ),
  ).toBe(true);
});
