import { afterEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { addMessage, resetMessagesForTests } from "@/shared/stores/messages";
import { renderInRouter } from "@/test/render";
import { ActivityIndicator } from "./activity-indicator";

// Spec 0015 AC-12, UX-24: the activity button's new message dot.

describe("ActivityIndicator", () => {
  afterEach(resetMessagesForTests);

  it("0015 AC-12: a message that arrives while it is open doesn't bring the dot back on close", async () => {
    addMessage({ tone: "success", message: "Table created", source: "Databases" });
    const { screen } = await renderInRouter(<ActivityIndicator />);
    const button = screen.getByRole("button", { name: "Activity, new messages" });
    await expect.element(button).toBeVisible();

    await button.click();
    await expect.element(screen.getByRole("dialog", { name: "Activity" })).toBeVisible();
    addMessage({ tone: "info", message: "Index built", source: "Databases" });
    await expect.element(screen.getByText("Index built")).toBeVisible();
    await userEvent.keyboard("{Escape}");

    await expect.element(screen.getByRole("dialog")).not.toBeInTheDocument();
    await expect.element(screen.getByRole("button", { name: "Activity" })).toBeVisible();

    addMessage({ tone: "error", message: "Backup failed", source: "Databases" });
    await expect
      .element(screen.getByRole("button", { name: "Activity, new messages" }))
      .toBeVisible();
  });
});
