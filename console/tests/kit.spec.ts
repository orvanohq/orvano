import { expect, test } from "@playwright/test";

import { hydrated, path } from "./helpers";

test.beforeEach(async ({ page }) => {
  await page.goto(path("/kit/"));
  await hydrated(page);
});

test("buttons and badges show every variant and state", async ({ page }) => {
  const buttons = page
    .getByRole("region", { name: "Buttons" })
    .getByRole("button", { name: "Create bucket" });
  await expect(buttons).toHaveCount(9);
  await expect(buttons.last()).toBeDisabled();
  const heights = await buttons.evaluateAll((nodes) =>
    nodes.map((node) => node.getBoundingClientRect().height),
  );
  expect(new Set(heights)).toEqual(new Set([32, 36]));

  const badges = page.getByRole("region", { name: "Badges" });
  for (const label of [
    "Draft",
    "Selected",
    "Deployed",
    "Pending approval",
    "Failed",
  ]) {
    await expect(badges.getByText(label, { exact: true })).toHaveCount(2);
  }
});

test("keyboard focus shows a visible brand ring", async ({ page }) => {
  const button = page
    .getByRole("region", { name: "Buttons" })
    .getByRole("button", { name: "Create bucket" })
    .first();
  await button.focus();
  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Tab");
  await expect(button).toBeFocused();
  const shadow = await button.evaluate(
    (node) => getComputedStyle(node).boxShadow,
  );
  expect(shadow).not.toBe("none");
});

test("a row's ID button opens a 480px side sheet that keeps and returns focus", async ({
  page,
}) => {
  const table = page.getByRole("region", { name: "Recent deliveries" });
  await expect(table.getByRole("row")).toHaveCount(26);
  const id = table.getByRole("button", { name: "dlv_01JF8K2M4Q03" });
  await id.focus();
  await page.keyboard.press("Enter");

  const sheet = page.getByRole("dialog", { name: "dlv_01JF8K2M4Q03" });
  await expect(sheet).toBeVisible();
  // The computed width, not the bounding box, which is fractional while the sheet slides in.
  await expect(sheet).toHaveCSS("width", "480px");
  await expect(sheet.getByText("Retrying", { exact: true })).toBeVisible();
  for (let i = 0; i < 6; i++) {
    await page.keyboard.press("Tab");
    expect(
      await sheet.evaluate((node) => node.contains(document.activeElement)),
    ).toBe(true);
  }
  await page.keyboard.press("Escape");
  await expect(sheet).toBeHidden();
  await expect(id).toBeFocused();
});

test("clicking anywhere on a row opens its sheet", async ({ page }) => {
  await page.getByRole("cell", { name: "400 ms" }).click();
  await expect(
    page.getByRole("dialog", { name: "dlv_01JF8K2M4Q05" }),
  ).toBeVisible();
});

test("the bucket name error shows inline, is linked and announced", async ({
  page,
}) => {
  const name = page.getByLabel("Bucket name");
  await page
    .getByRole("region", { name: "Form" })
    .getByRole("button", { name: "Create bucket" })
    .click();
  const error = page.getByText(
    "Use 3 to 63 lowercase letters, numbers or dashes.",
  );
  await expect(error).toBeVisible();
  await expect(error).toHaveAttribute("aria-live", "polite");
  await expect(name).toHaveAttribute("aria-invalid", "true");
  await expect(name).toHaveAccessibleDescription(
    /Use 3 to 63 lowercase letters/,
  );

  await name.fill("recipes-dev");
  await page.getByLabel("Region").click();
  await page.getByRole("option", { name: "s3" }).click();
  await page.getByLabel("Public bucket").check();
  await page
    .getByRole("region", { name: "Form" })
    .getByRole("button", { name: "Create bucket" })
    .click();
  await expect(name).not.toHaveAttribute("aria-invalid");
  await expect(
    page.getByRole("region", { name: "Form" }).locator("pre"),
  ).toContainText('"region": "s3"');
});

test("the destructive button enables only when the name is typed", async ({
  page,
}) => {
  const section = page.getByRole("region", {
    name: "Destructive confirmation",
  });
  const remove = section.getByRole("button", { name: "Delete bucket" });
  await expect(remove).toBeDisabled();
  await section.getByLabel("Type recipes-dev to confirm").fill("recipes-de");
  await expect(remove).toBeDisabled();
  await section.getByLabel("Type recipes-dev to confirm").fill("recipes-dev");
  await expect(remove).toBeEnabled();
  await remove.click();
  await expect(section.getByRole("status")).toHaveText(
    "Nothing was deleted: this is the kit page.",
  );
});

test("the code block copies its code and announces it", async ({
  page,
  context,
}) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  const block = page
    .getByRole("region", { name: "Code block" })
    .locator("figure")
    .first();
  await block.getByRole("button", { name: "Copy code" }).click();
  await expect(block.getByRole("status")).toHaveText("Copied");
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(
    "select id, title from recipes where author_id = $1 limit 20;",
  );
  await expect(block.getByRole("status")).toHaveText("", { timeout: 4000 });
});

test("a code block wider than its box is a labelled tab stop that scrolls by keyboard", async ({
  page,
}) => {
  // The short SQL block fits, so it adds no tab stop.
  const sql = page
    .getByRole("region", { name: "Code block" })
    .locator("pre")
    .first();
  await expect(sql).not.toHaveAttribute("tabindex");

  await page.getByRole("button", { name: "dlv_01JF8K2M4Q02" }).click();
  const sheet = page.getByRole("dialog", { name: "dlv_01JF8K2M4Q02" });
  await expect(sheet).toHaveCSS("width", "480px");
  const json = sheet.getByRole("group", { name: "json code" });
  await sheet.getByRole("button", { name: "Copy code" }).focus();
  await page.keyboard.press("Tab");
  await expect(json).toBeFocused();
  await page.keyboard.press("End");
  await page.keyboard.press("ArrowRight");
  await expect
    .poll(() => json.evaluate((node) => node.scrollLeft))
    .toBeGreaterThan(0);
});
