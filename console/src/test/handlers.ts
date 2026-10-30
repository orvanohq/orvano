import { HttpResponse } from "msw";
import { createOpenApiHttp } from "openapi-msw";
import type { paths } from "@/shared/api/schema.gen";

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

/**
 * The shared defaults every test starts with (QA-2): boot's config call, with setup done. A test adds its own with
 * `worker.use(...)`, which the next test no longer sees.
 */
export const defaultHandlers = [
  http.get("/v1/console/config", ({ response }) => response(200).json({ setupRequired: false })),
];
