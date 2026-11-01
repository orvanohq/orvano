import { describe, expect, it, vi } from "vitest";
import { i18n } from "@/shared/i18n";
import { http, shellHandlers, testSession } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

// Only English ships, so the Language field stays hidden (account-page.test.tsx). A second locale shows it.
vi.mock("@/shared/i18n/locales", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/i18n/locales")>()),
  hasLanguageChoice: true,
  shippedLocales: ["en", "pseudo-LOCALE"],
}));

const saved = { ...testSession.operator, version: "AAAAAAAAAAI" };

// Spec 0017 ACC-1, LOC-3, AC-19.
describe("the Language field with a second locale", () => {
  it("0017 AC-19: the language saves, and Use my browser sends null", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", async ({ request, response }) => {
        const body = (await request.clone().json()) as { locale: string | null };
        sent.push(body);
        return response(200).json({ ...saved, locale: body.locale });
      }),
    );
    const screen = await renderWithProviders("/account", {
      session: { ...testSession, operator: { ...testSession.operator, locale: "en" } },
    });
    const card = screen.getByRole("region", { name: "Language" });

    await card.getByRole("button", { name: /English/ }).click();
    await screen.getByRole("option", { name: "Use my browser" }).click();
    await card.getByRole("button", { name: "Update" }).click();

    await vi.waitFor(() => {
      expect(sent).toEqual([{ locale: null }]);
    });
    expect(i18n.locale).toBe("en");
  });
});
