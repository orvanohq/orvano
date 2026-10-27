import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { Avatar, initials } from "./avatar";
import { Button } from "./button";
import { Icon } from "./icon";
import { ChevronStartIcon, ServiceDatabasesIcon } from "./icons";
import { Tooltip } from "./tooltip";
import { VisuallyHidden } from "./visually-hidden";

describe("Tooltip", () => {
  it("0015 AC-15: opens on keyboard focus and closes with Escape", async () => {
    const { screen } = await renderInRouter(
      <Tooltip content="Saves the row">
        <Button>Save</Button>
      </Tooltip>,
    );

    // The first Tab in a fresh Firefox frame can land on the frame itself, so Tab until the button has focus.
    const button = screen.getByRole("button", { name: "Save" });
    for (let presses = 0; presses < 3 && document.activeElement !== button.element(); presses++) {
      // oxlint-disable-next-line no-await-in-loop -- each Tab depends on where the last one put focus
      await userEvent.tab();
    }
    await expect.element(button).toHaveFocus();
    await expect.element(screen.getByRole("tooltip")).toHaveTextContent("Saves the row");
    await expectNoAxeViolations();
    await userEvent.keyboard("{Escape}");

    await expect.element(screen.getByRole("tooltip")).not.toBeInTheDocument();
    await expect.element(button).toHaveFocus();
  });
});

describe("Icon", () => {
  it("0015 AC-15: is hidden from assistive technology unless labeled", async () => {
    const { screen } = await renderInRouter(
      <p>
        <Icon icon={ServiceDatabasesIcon} />
        <Icon icon={ServiceDatabasesIcon} size={24} label="Databases" />
      </p>,
    );

    await expect.element(screen.getByRole("img", { name: "Databases" })).toBeVisible();
    expect(document.querySelectorAll("svg[aria-hidden='true']")).toHaveLength(1);
    await expectNoAxeViolations();
  });

  it("0015 AC-18: mirrors a direction icon under dir=rtl", async () => {
    const { screen } = await renderInRouter(
      <div dir="rtl">
        <Icon icon={ChevronStartIcon} flipInRtl label="Back" />
      </div>,
    );
    const icon = screen.getByRole("img", { name: "Back" });

    await expect.element(icon).toBeVisible();
    // Tailwind 4's -scale-x-100 sets the `scale` property, not `transform`.
    expect(getComputedStyle(icon.element()).scale.startsWith("-1")).toBe(true);
  });
});

describe("Avatar", () => {
  it("shows the first letters of the first and last words, uppercased", () => {
    expect(initials("ada king lovelace", "ada@example.com", "en")).toBe("AL");
    expect(initials("Émile", "emile@example.com", "en")).toBe("É");
    expect(initials("  ", "grace@example.com", "en")).toBe("G");
    expect(initials("istanbul ilçe", "x@example.com", "tr")).toBe("İİ");
  });

  it("0015 AC-15: is decorative and passes axe", async () => {
    await renderInRouter(<Avatar name="Ada Lovelace" email="ada@example.com" />);

    const avatar = document.querySelector("[aria-hidden='true']");
    expect(avatar?.getAttribute("data-initials")).toBe("AL");
    expect(getComputedStyle(avatar as Element, "::before").content).toBe('"AL"');
    await expectNoAxeViolations();
  });
});

describe("VisuallyHidden", () => {
  it("keeps its text in the accessibility tree but off screen", async () => {
    const { screen } = await renderInRouter(
      <button type="button">
        <VisuallyHidden>Close dialog</VisuallyHidden>
      </button>,
    );

    await expect.element(screen.getByRole("button", { name: "Close dialog" })).toBeInTheDocument();
  });
});
