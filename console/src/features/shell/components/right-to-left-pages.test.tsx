import { afterEach, describe, expect, it } from "vitest";
import { page, userEvent } from "vitest/browser";
import { messages as sourceMessages } from "@/locales/en/messages.po";
import type { components } from "@/shared/api/schema.gen";
import { activateLocale, getDirection, i18n } from "@/shared/i18n";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import {
  http,
  shellHandlers,
  testOrgId,
  testProject,
  testProjectId,
  testSession,
} from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

const project = `/${testOrgId}/${testProjectId}`;

const secretKey = {
  id: "01970000-0000-7000-8000-00000000000c",
  projectId: testProjectId,
  type: "secret",
  name: "Build server",
  prefix: "orv_sk_AAAAAAAA",
  last4: "Zz09",
  scopes: ["databases.read"],
  status: "active",
  lastUsedAt: null,
  expiresAt: null,
  revokedAt: null,
  createdAt: "2030-01-01T00:00:00Z",
  allowedActions: ["keys.write"],
} satisfies components["schemas"]["ApiKeyResponse"];

/** Every new page (CNS-1), with what it shows, so the empty pages and the lists are both seen. */
const pages: {
  name: string;
  url: string;
  heading: string;
  session?: null;
  setup?: boolean;
  projects?: components["schemas"]["ProjectResponse"][];
  keys?: components["schemas"]["ApiKeyResponse"][];
}[] = [
  { name: "sign in", url: "/sign-in", session: null, heading: "Sign in" },
  { name: "reset", url: "/reset", session: null, heading: "Reset your password" },
  { name: "setup", url: "/setup", session: null, heading: "Set up Orvano", setup: true },
  { name: "projects", url: `/${testOrgId}`, heading: "Projects", projects: [testProject] },
  { name: "first project", url: `/${testOrgId}`, heading: "Projects" },
  {
    name: "organization settings",
    url: `/${testOrgId}/settings`,
    heading: "Organization settings",
  },
  { name: "overview", url: project, heading: "Shop" },
  { name: "general", url: `${project}/settings/general`, heading: "Project settings" },
  { name: "keys", url: `${project}/settings/keys`, heading: "Project settings", keys: [secretKey] },
  { name: "first key", url: `${project}/settings/keys`, heading: "Project settings" },
  { name: "origins", url: `${project}/settings/origins`, heading: "Project settings" },
  { name: "account", url: "/account", heading: "Account" },
];

function handlers({ projects = [], keys = [] }: Pick<(typeof pages)[number], "projects" | "keys">) {
  return [
    http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) =>
      response(200).json({ items: projects, nextCursor: null, total: null }),
    ),
    http.get("/v1/console/projects/{projectId}/keys", ({ response }) =>
      response(200).json({ items: keys, nextCursor: null, total: null }),
    ),
    ...shellHandlers,
  ];
}

/**
 * Spec 0015 RTL-1: right to left with no catalog of its own. React Aria gets the real `ar`, and Lingui keeps the English
 * messages under that tag. Never the pseudo `ar-XB`: CI's WebKit resolves it as left to right.
 */
function activateRightToLeft() {
  i18n.loadAndActivate({ locale: "ar", messages: sourceMessages });
  document.documentElement.lang = "ar";
  document.documentElement.dir = getDirection("ar");
}

function open(item: (typeof pages)[number]) {
  worker.use(...handlers(item));
  return renderWithProviders(item.url, {
    config: { setupRequired: item.setup ?? false },
    session: "session" in item ? item.session : testSession,
  });
}

afterEach(async () => {
  delete document.documentElement.dataset.theme;
  await page.viewport(414, 896); // Vitest's default browser viewport
  await activateLocale("en");
});

// Spec 0017 LOC-5, AC-23.
describe.each(["light", "dark"] as const)("right to left in the %s theme", (theme) => {
  it.each(pages)("0017 LOC-5: $name reads right to left and passes axe", async (item) => {
    document.documentElement.dataset.theme = theme;
    activateRightToLeft();
    const screen = await open(item);

    await expect
      .element(screen.getByRole("heading", { level: 1, name: item.heading }))
      .toBeVisible();
    expect(getComputedStyle(document.body).direction).toBe("rtl");
    await expectNoAxeViolations(document.body);
  });

  it("0017 LOC-5: the owner form reads right to left and passes axe", async () => {
    document.documentElement.dataset.theme = theme;
    worker.use(
      http.post("/v1/console/setup/token", () => new Response(null, { status: 204 })),
      ...shellHandlers,
    );
    activateRightToLeft();
    const screen = await renderWithProviders("/setup", {
      config: { setupRequired: true },
      session: null,
    });

    await focusElement(screen.getByRole("textbox", { name: "Setup token" }));
    await userEvent.keyboard("orv_setup_token");
    await screen.getByRole("button", { name: "Continue" }).click();

    await expect
      .element(screen.getByRole("button", { name: "Create owner account" }))
      .toBeVisible();
    // Not the body: React Aria's announcer still points at the token step's pending button (src/ui/announcer.ts).
    await expectNoAxeViolations(screen.container);
  });
});

// Spec 0017 AC-23, spec 0015 GAL-2: the pseudo locale's 35% longer text, on a phone.
describe("the pseudo locale", () => {
  it.each(pages)("0001 UX-34: $name has no sideways scroll at 375 wide", async (item) => {
    await page.viewport(375, 812);
    await activateLocale("pseudo-LOCALE");
    const screen = await open(item);

    await expect.element(screen.getByRole("heading", { level: 1 })).toBeVisible();
    const root = document.documentElement;
    expect(root.scrollWidth).toBeLessThanOrEqual(root.clientWidth);
  });
});
