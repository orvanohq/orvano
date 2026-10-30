import { describe, expect, it } from "vitest";
import { renderWithProviders } from "@/test/render";

// Spec 0016 FRS-4: the setup page sits in AuthLayout, like the other pages without the shell.
describe("SetupPage", () => {
  it("shows its heading as the page h1 inside a main landmark", async () => {
    const screen = await renderWithProviders("/setup", { config: { setupRequired: true } });

    await expect
      .element(screen.getByRole("main").getByRole("heading", { level: 1, name: "Set up Orvano" }))
      .toBeVisible();
  });
});
