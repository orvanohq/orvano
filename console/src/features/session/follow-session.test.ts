import { QueryClient } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import { reloadConsole } from "@/shared/session-channel";
import { sessionEnded } from "@/shared/stores/session";
import { http, problem, testSession } from "@/test/handlers";
import { worker } from "@/test/msw";
import { sessionQuery } from "./api/queries";
import { followSession } from "./follow-session";

vi.mock("@/shared/session-channel", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/session-channel")>()),
  reloadConsole: vi.fn<() => void>(),
}));

afterEach(() => {
  sessionEnded.set(false);
  vi.mocked(reloadConsole).mockClear();
});

function clientWith(session: typeof testSession | null) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(sessionQuery().queryKey, session);
  return queryClient;
}

// Spec 0017 ACC-2, SES-9: a tab follows a session another tab changed.
describe("following another tab's session", () => {
  it("0017 ACC-2: takes the new CSRF token, and closes a sign in dialog", async () => {
    const rotated = { ...testSession, csrfToken: "bmV3LWNzcmYtdG9rZW4" };
    worker.use(http.get("/v1/console/session", ({ response }) => response(200).json(rotated)));
    const queryClient = clientWith(testSession);
    sessionEnded.set(true);

    await followSession(queryClient);

    expect(queryClient.getQueryData(sessionQuery().queryKey)?.csrfToken).toBe(rotated.csrfToken);
    expect(sessionEnded.get()).toBe(false);
  });

  it("0003 QRY-8: someone else's session reloads the page", async () => {
    worker.use(
      http.get("/v1/console/session", ({ response }) =>
        response(200).json({
          ...testSession,
          operator: { ...testSession.operator, id: "01970000-0000-7000-8000-000000000002" },
        }),
      ),
    );

    await followSession(clientWith(testSession));

    expect(reloadConsole).toHaveBeenCalledOnce();
  });

  it("0017 ACC-2: a signed out tab has nothing to follow", async () => {
    const queryClient = clientWith(null);

    await followSession(queryClient);

    expect(queryClient.getQueryData(sessionQuery().queryKey)).toBeNull();
  });

  it("0017 ACC-2: a failed read leaves the tab's session as it is", async () => {
    worker.use(http.get("/v1/console/session", () => problem(500, "internal")));
    const queryClient = clientWith(testSession);

    await expect(followSession(queryClient)).rejects.toThrow("Test problem.");

    expect(queryClient.getQueryData(sessionQuery().queryKey)).toEqual(testSession);
  });
});
