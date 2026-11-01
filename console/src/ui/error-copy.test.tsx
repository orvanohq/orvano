import { afterEach, describe, expect, it } from "vitest";
import { ApiError, kindFromStatus } from "@/shared/api/errors";
import { activateLocale } from "@/shared/i18n";
import { renderInRouter } from "@/test/render";
import { InlineError } from "./banner";

const detail = "English detail from the server.";

function failure(status: number, code: string) {
  return new ApiError({ kind: kindFromStatus(status), status, code, message: detail });
}

afterEach(async () => {
  await activateLocale("en");
});

// Spec 0017 LOC-4, spec 0003 FLT-1: known codes read in the console's own words, so an error follows the catalog, and
// the server's English `detail` never shows. The pseudo locale marks every message it translates.
describe("errors by code", () => {
  it.each([
    [400, "validation_failed"],
    [401, "session_expired"],
    [403, "forbidden"],
    [403, "reauth_required"],
    [404, "not_found"],
    [409, "project_not_active"],
    [409, "api_key_not_active"],
    [412, "version_conflict"],
    [429, "rate_limited"],
    [500, "internal"],
    [503, "unavailable"],
  ])("0017 LOC-4: %i %s reads from the catalog", async (status, code) => {
    await activateLocale("pseudo-LOCALE");
    const { screen } = await renderInRouter(<InlineError error={failure(status, code)} />);

    const alert = screen.getByRole("alert");
    await expect.poll(() => alert.element().textContent).toContain("[~");
    expect(alert.element().textContent).not.toContain(detail);
  });

  it("0017 LOC-4: a refused regenerate says why, not that someone else changed it", async () => {
    const { screen } = await renderInRouter(
      <InlineError error={failure(409, "api_key_not_active")} />,
    );

    await expect.element(screen.getByText("Only an active key can be regenerated.")).toBeVisible();
  });

  it("0017 LOC-4: a change to a project being deleted says so", async () => {
    const { screen } = await renderInRouter(
      <InlineError error={failure(409, "project_not_active")} />,
    );

    await expect
      .element(screen.getByText("This project is being deleted, so it can't change."))
      .toBeVisible();
  });
});
