import { HttpResponse } from "msw";
import { createOpenApiHttp } from "openapi-msw";
import type { ConsoleConfig } from "@/features/console-config";
import type { Session } from "@/features/session";
import type { components, paths } from "@/shared/api/schema.gen";

/** Spec 0003 QA-2: MSW handlers typed from the console contract, so a handler for a path the API lacks fails types. */
export const http = createOpenApiHttp<paths>();

/** A problem details answer (spec 0014 PRB-1) with its code, as the server sends it. */
export function problem(
  status: number,
  code: string,
  extra: { detail?: string; retryAfter?: number; headers?: Record<string, string> } = {},
): Response {
  const { headers, ...members } = extra;
  return HttpResponse.json(
    {
      type: "about:blank",
      title: "Problem",
      status,
      detail: members.detail ?? "Test problem.",
      code,
      traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
      ...(members.retryAfter === undefined ? {} : { retryAfter: members.retryAfter }),
    },
    { status, headers: { "Content-Type": "application/problem+json", ...headers } },
  );
}

/** The boot config tests start from, with setup done. */
export const testConfig = {
  setupRequired: false,
  instanceName: "Orvano",
  publicUrl: "https://orvano.example.com",
  features: [],
} satisfies ConsoleConfig;

/** A signed in owner with one organization. */
export const testSession = {
  operator: {
    id: "01970000-0000-7000-8000-000000000001",
    name: "Ada Owner",
    email: "ada@example.com",
    locale: null,
    version: "AAAAAAAAAAE",
  },
  memberships: [
    {
      organizationId: "01970000-0000-7000-8000-00000000000a",
      organizationName: "Acme",
      role: "owner",
      allowedActions: ["org.read", "projects.create"],
    },
  ],
  csrfToken: "dGVzdC1jc3JmLXRva2VuLWZvci10aGUtY29uc29sZS1zZXNzaW9u",
  expiresAt: "2030-01-01T00:00:00Z",
} satisfies Session;

/**
 * The shared defaults every test starts with (QA-2): boot's config call, with setup done. A test adds its own with
 * `worker.use(...)`, which the next test no longer sees.
 */
export const defaultHandlers = [
  http.get("/v1/console/config", ({ response }) =>
    response(200).json({ ...testConfig, setupRequired: false }),
  ),
];

export const testOrgId = testSession.memberships[0]?.organizationId ?? "";
export const testProjectId = "01970000-0000-7000-8000-00000000000b";

const allowedProjectActions = ["project.read", "project.write", "keys.read", "keys.write"];

/** The answers an app page needs for the session's organization and one project, for a test to `worker.use(...)`. */
export const shellHandlers = [
  http.get("/v1/console/organizations/{organizationId}", ({ response }) =>
    response(200).json({
      id: testOrgId,
      name: "Acme",
      allowedActions: ["org.read", "projects.create"],
      version: "AAAAAAAAAAE",
      createdAt: "2030-01-01T00:00:00Z",
      updatedAt: "2030-01-01T00:00:00Z",
    }),
  ),
  http.get("/v1/console/projects/{projectId}", ({ response }) => response(200).json(testProject)),
  http.get("/v1/console/projects/{projectId}/keys", ({ response }) =>
    response(200).json({ items: [], nextCursor: null, total: null }),
  ),
];

export const testProject = {
  id: testProjectId,
  organizationId: testOrgId,
  name: "Shop",
  status: "active",
  allowedOrigins: [],
  services: [],
  allowedActions: allowedProjectActions,
  version: "AAAAAAAAAAE",
  createdAt: "2030-01-01T00:00:00Z",
  updatedAt: "2030-01-01T00:00:00Z",
} satisfies components["schemas"]["ProjectResponse"];
