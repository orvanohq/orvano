import type { Page, Route } from "@playwright/test";

/** A 401 as the server sends it, with its problem code. */
export function unauthorized(route: Route, code = "unauthorized") {
  return route.fulfill({
    status: 401,
    contentType: "application/problem+json",
    headers: { "Cache-Control": "no-store" },
    json: {
      type: "about:blank",
      title: "Unauthorized",
      status: 401,
      detail: "The credential is missing.",
      code,
      traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
    },
  });
}

/** vite preview has no API: answers boot's session call (spec 0017 BOT-2) as signed out. */
export async function mockSignedOut(page: Page) {
  await page.route("**/v1/console/session", (route) => unauthorized(route));
}

export const ORG_ID = "01970000-0000-7000-8000-00000000000a";
export const PROJECT_ID = "01970000-0000-7000-8000-00000000000b";

const stamps = {
  version: "AAAAAAAAAAE",
  createdAt: "2030-01-01T00:00:00Z",
  updatedAt: "2030-01-01T00:00:00Z",
};
const emptyPage = { items: [], nextCursor: null, total: null };

export const SESSION = {
  operator: {
    id: "01970000-0000-7000-8000-000000000001",
    name: "Ada",
    email: "ada@example.com",
    locale: null,
    version: stamps.version,
  },
  memberships: [
    { organizationId: ORG_ID, organizationName: "Acme", role: "owner", allowedActions: [] },
  ],
  csrfToken: "dGVzdA",
  expiresAt: "2030-01-01T00:00:00Z",
};

/** Answers the reads the shell's pages make for a signed in owner with one organization and one project. */
export async function mockConsoleApi(page: Page) {
  await page.route("**/v1/console/config", (route) =>
    route.fulfill({
      json: {
        setupRequired: false,
        instanceName: "Orvano",
        publicUrl: "http://localhost:4173",
        features: [],
      },
    }),
  );
  await page.route("**/v1/console/session", (route) => route.fulfill({ json: SESSION }));
  await page.route(`**/v1/console/organizations/${ORG_ID}`, (route) =>
    route.fulfill({ json: { id: ORG_ID, name: "Acme", allowedActions: [], ...stamps } }),
  );
  await page.route(`**/v1/console/organizations/${ORG_ID}/projects*`, (route) =>
    route.fulfill({ json: emptyPage }),
  );
  await page.route(`**/v1/console/projects/${PROJECT_ID}`, (route) =>
    route.fulfill({
      json: {
        id: PROJECT_ID,
        organizationId: ORG_ID,
        name: "Shop",
        status: "active",
        allowedOrigins: [],
        services: [],
        allowedActions: [],
        ...stamps,
      },
    }),
  );
  await page.route(`**/v1/console/projects/${PROJECT_ID}/keys*`, (route) =>
    route.fulfill({ json: emptyPage }),
  );
}
