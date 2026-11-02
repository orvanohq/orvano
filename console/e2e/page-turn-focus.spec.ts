import { expect, test, type Page } from "@playwright/test";
import { deferred } from "../src/test/deferred";
import { mockConsoleApi, ORG_ID, PROJECT_ID } from "./mocks";

// Spec 0001 UX-21, spec 0003 RTE-4, QRY-5: paging a slow list keeps its rows and its pager on screen, so focus stays on
// the control you pressed. Run on the build, because Vitest's iframe can't show where a real document's focus falls.

const CREATED_AT = "2030-01-01T00:00:00Z";
const pages = ["First", "Second", "Third"];

const lists = [
  {
    title: "the keys page",
    table: "API keys",
    path: `${ORG_ID}/${PROJECT_ID}/settings/keys`,
    calls: `**/v1/console/projects/${PROJECT_ID}/keys*`,
    item: (name: string) => ({
      id: `01970000-0000-7000-8000-0000000001${String(pages.indexOf(name))}0`,
      projectId: PROJECT_ID,
      type: "secret",
      name,
      prefix: "orv_sk_dGhpcyBp",
      last4: "abcd",
      scopes: ["storage.read"],
      status: "active",
      lastUsedAt: null,
      expiresAt: null,
      revokedAt: null,
      createdAt: CREATED_AT,
      allowedActions: ["keys.read", "keys.write"],
    }),
  },
  {
    title: "the organization page",
    table: "Projects",
    path: ORG_ID,
    calls: `**/v1/console/organizations/${ORG_ID}/projects*`,
    item: (name: string) => ({
      id: `01970000-0000-7000-8000-0000000002${String(pages.indexOf(name))}0`,
      organizationId: ORG_ID,
      name,
      status: "active",
      allowedOrigins: [],
      services: [],
      allowedActions: [],
      version: "AAAAAAAAAAE",
      createdAt: CREATED_AT,
      updatedAt: CREATED_AT,
    }),
  },
];

type List = (typeof lists)[number];

/** Answers the list with three pages. The returned function holds every API call until its own result is called. */
async function pagedList(page: Page, list: List) {
  let gate: Promise<undefined> | undefined;
  await page.route(list.calls, async (route) => {
    const cursor = new URL(route.request().url()).searchParams.get("cursor");
    const index = cursor === null ? 0 : Number(cursor.replace("page-", "")) - 1;
    await route.fulfill({
      json: {
        items: [list.item(pages[index] ?? "First")],
        nextCursor: index < pages.length - 1 ? `page-${String(index + 2)}` : null,
        total: null,
      },
    });
  });
  await page.route("**/v1/console/**", async (route) => {
    await gate;
    await route.fallback();
  });
  return () => {
    const held = deferred<undefined>();
    gate = held.promise;
    return () => {
      gate = undefined;
      held.resolve(undefined);
    };
  };
}

const row = (page: Page, name: string) => page.getByRole("rowheader", { name });
const pager = (page: Page) => page.getByRole("navigation", { name: "Pagination" });
const pageButton = (page: Page, name: string) => pager(page).getByRole("button", { name });

/** Past UX-21's 300 ms and the time a skeleton would take to paint, so a test can't pass before one could show. */
const pastSkeletonDelay = (page: Page) => page.waitForTimeout(600);

/** What's true all the time a page is on its way, whichever control asked for it. */
async function expectRowsKept(page: Page, list: List, name: string) {
  await pastSkeletonDelay(page);
  await expect(row(page, name)).toBeVisible();
  await expect(page.getByRole("region", { name: list.table })).toHaveAttribute("aria-busy", "true");
  await expect(page.getByText("Loading", { exact: true })).toHaveCount(0);
}

for (const list of lists) {
  test.describe(list.title, () => {
    test.beforeEach(async ({ page }) => {
      await mockConsoleApi(page);
    });

    test("0001 UX-21: a slow Next keeps the rows and focus, and a second press does nothing", async ({
      page,
    }) => {
      const hold = await pagedList(page, list);
      await page.goto(list.path);
      await expect(row(page, "First")).toBeVisible();
      const next = pageButton(page, "Next");
      await next.focus();
      const release = hold();

      await page.keyboard.press("Enter");

      await expectRowsKept(page, list, "First");
      await expect(next).toBeFocused();
      await page.keyboard.press("Enter");

      release();
      await expect(row(page, "Second")).toBeVisible();
      await expect(row(page, "First")).toBeHidden();
      await expect(next).toBeFocused();
      await expect(page.getByRole("region", { name: list.table })).not.toHaveAttribute(
        "aria-busy",
        "true",
      );
      expect(new URL(page.url()).searchParams.get("cursor")).toBe("page-2");

      await page.keyboard.press("Shift+Tab");
      await expect(pageButton(page, "Previous")).toBeFocused();
      await page.keyboard.press("Enter");
      await expect(row(page, "First")).toBeVisible(); // one press, one page
    });

    test("0001 UX-21: a slow Previous keeps the rows and focus", async ({ page }) => {
      const hold = await pagedList(page, list);
      await page.goto(`${list.path}?cursor=page-2`); // a pasted link, so the cache has no first page
      await expect(row(page, "Second")).toBeVisible();
      const previous = pageButton(page, "Previous");
      await previous.focus();
      const release = hold();

      await page.keyboard.press("Enter");

      await expectRowsKept(page, list, "Second");
      await expect(previous).toBeFocused();

      release();
      await expect(row(page, "First")).toBeVisible();
      await expect(previous).toBeFocused();
    });

    test("0001 UX-21: a slow page size keeps the rows and focus", async ({ page }) => {
      const hold = await pagedList(page, list);
      await page.goto(`${list.path}?cursor=page-2`);
      await expect(row(page, "Second")).toBeVisible();
      const release = hold();

      await pager(page).getByRole("button", { name: "50 per page" }).focus();
      await page.keyboard.press("Enter");
      await page.getByRole("menuitemradio", { name: "25" }).focus();
      await page.keyboard.press("Enter");

      const size = pager(page).getByRole("button", { name: "25 per page" });
      await expectRowsKept(page, list, "Second");
      await expect(size).toBeFocused();

      release();
      await expect(row(page, "First")).toBeVisible();
      await expect(size).toBeFocused();
    });

    test("0001 UX-21: a slow Back keeps the rows and focus", async ({ page }) => {
      const hold = await pagedList(page, list);
      await page.goto(list.path);
      await pageButton(page, "Next").click();
      await expect(row(page, "Second")).toBeVisible();
      await page.reload(); // empties the cache, and Back still has its entry
      await expect(row(page, "Second")).toBeVisible();
      const next = pageButton(page, "Next");
      await next.focus();
      const release = hold();

      await page.goBack({ waitUntil: "commit" });

      await expectRowsKept(page, list, "Second");
      await expect(next).toBeFocused();

      release();
      await expect(row(page, "First")).toBeVisible();
      await expect(next).toBeFocused();
    });

    test("0003 RTE-4: on a slow link, frame data past its stale time doesn't hold up the turn", async ({
      page,
    }) => {
      await page.clock.install();
      const hold = await pagedList(page, list);
      await page.goto(list.path);
      await expect(row(page, "First")).toBeVisible();
      await page.clock.fastForward(31_000); // past the 30 s `staleTime` of `query-client.ts`
      const next = pageButton(page, "Next");
      await next.focus();
      const release = hold();

      await page.keyboard.press("Enter");

      await expectRowsKept(page, list, "First");
      await expect(next).toBeFocused();

      release();
      await expect(row(page, "Second")).toBeVisible();
      await expect(next).toBeFocused();
    });

    test("0017 AC-21: a slow first load still shows the skeleton", async ({ page }) => {
      await pagedList(page, list);
      const held = deferred<undefined>();
      await page.route(list.calls, async (route) => {
        await held.promise; // only the list: boot needs its own calls to get this far
        await route.fallback();
      });

      await page.goto(list.path, { waitUntil: "commit" });

      await expect(page.getByText("Loading", { exact: true })).toBeAttached();
      await expect(page.getByRole("region", { name: list.table })).toBeHidden();

      held.resolve(undefined);
      await expect(row(page, "First")).toBeVisible();
      await expect(page.getByText("Loading", { exact: true })).toHaveCount(0);
    });
  });
}
