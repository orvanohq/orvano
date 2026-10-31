import { useState } from "react";
import { msg } from "@lingui/core/macro";
import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { emulateForcedColors } from "@/test/media";
import { renderInRouter } from "@/test/render";
import { Button, IconButton } from "./button";
import { CloseIcon } from "./icons";

const reason = msg`You need the Admin role to delete tables.`;

function SavesOnce() {
  const [isSaved, setSaved] = useState(false);
  return (
    <Button
      unavailableReason={isSaved ? reason : undefined}
      reasonMayChange
      onPress={() => {
        setSaved(true);
      }}
    >
      Update
    </Button>
  );
}

describe("Button", () => {
  it("0003 FRM-5: keeps focus when an unavailable reason arrives", async () => {
    const { screen } = await renderInRouter(<SavesOnce />);
    const button = screen.getByRole("button", { name: "Update" });
    await focusElement(button);

    await userEvent.keyboard("{Enter}");

    await expect.element(button).toHaveAttribute("aria-disabled", "true");
    await expect.element(button).toHaveFocus();
  });

  it("0015 AC-15: every variant and size passes axe in both themes", async () => {
    const { screen } = await renderInRouter(
      <div>
        {(["primary", "secondary", "ghost", "danger"] as const).map((variant) =>
          (["sm", "md", "lg"] as const).map((size) => (
            <Button key={`${variant}-${size}`} variant={variant} size={size}>
              Create {variant} {size}
            </Button>
          )),
        )}
      </div>,
    );
    await expect.element(screen.getByRole("button", { name: "Create primary md" })).toBeVisible();
    for (const theme of ["light", "dark"]) {
      document.documentElement.dataset.theme = theme;
      await expectNoAxeViolations();
    }
    delete document.documentElement.dataset.theme;
  });

  it("0015 AC-6: in forced colors mode every bordered variant keeps a visible border (THM-6)", async () => {
    await emulateForcedColors(true);
    try {
      // Ghost is borderless in every mode by design (design.md _Button recipes_).
      const { screen } = await renderInRouter(
        <div>
          {(["primary", "secondary", "danger"] as const).map((variant) => (
            <Button key={variant} variant={variant}>
              Create {variant}
            </Button>
          ))}
        </div>,
      );
      for (const variant of ["primary", "secondary", "danger"]) {
        const button = screen.getByRole("button", { name: `Create ${variant}` });
        await expect.element(button).toBeVisible();
        const style = getComputedStyle(button.element());
        expect({ variant, style: style.borderTopStyle }).toEqual({ variant, style: "solid" });
        expect(Number.parseFloat(style.borderTopWidth)).toBeGreaterThanOrEqual(1);
      }
    } finally {
      await emulateForcedColors(false);
    }
  });

  it("0015 AC-15: presses with Enter, Space, and a click", async () => {
    const onPress = vi.fn<() => void>();
    const { screen } = await renderInRouter(<Button onPress={onPress}>Save</Button>);

    const button = screen.getByRole("button", { name: "Save" });
    await userEvent.click(button, { force: true });
    // A click doesn't focus a button in every engine, so put focus there before the keys (CI's Firefox).
    await focusElement(button);
    await userEvent.keyboard("{Enter}");
    await userEvent.keyboard(" ");

    expect(onPress).toHaveBeenCalledTimes(3);
  });

  it("0015 AC-15: an unavailable button stays focusable, ignores presses, and exposes its reason (PRM-4)", async () => {
    const onPress = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <Button variant="danger" onPress={onPress} unavailableReason={reason}>
        Delete table
      </Button>,
    );
    const button = screen.getByRole("button", { name: "Delete table" });

    await userEvent.tab();
    await expect.element(button).toHaveFocus();
    await userEvent.keyboard("{Enter}");
    await userEvent.keyboard(" ");
    await userEvent.click(button, { force: true });

    expect(onPress).not.toHaveBeenCalled();
    await expect.element(button).toHaveAttribute("aria-disabled", "true");
    await expect.element(button).toHaveAccessibleDescription(reason.message ?? "");
    await expectNoAxeViolations();
  });

  it("0015 AC-15: an unavailable submit button doesn't submit its form", async () => {
    const onSubmit = vi.fn<(event: SubmitEvent) => void>((event) => {
      event.preventDefault();
    });
    const { screen } = await renderInRouter(
      <form
        aria-label="Table"
        onSubmit={(event) => {
          onSubmit(event.nativeEvent);
        }}
      >
        <Button type="submit" unavailableReason={reason}>
          Save
        </Button>
      </form>,
    );

    await userEvent.click(screen.getByRole("button", { name: "Save" }), { force: true });

    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("0015 AC-15: shows the reason in a tooltip on keyboard focus", async () => {
    const { screen } = await renderInRouter(
      <Button unavailableReason={reason}>Delete table</Button>,
    );

    await userEvent.tab();

    await expect.element(screen.getByRole("tooltip")).toHaveTextContent(reason.message ?? "");
  });

  it("a disabled button can't be focused", async () => {
    const { screen } = await renderInRouter(
      <>
        <Button isDisabled>Saving</Button>
        <Button>Next</Button>
      </>,
    );

    await userEvent.tab();

    await expect.element(screen.getByRole("button", { name: "Next" })).toHaveFocus();
  });
});

describe("IconButton", () => {
  it("0015 AC-15: takes its name from aria-label, shows it as a tooltip, and passes axe", async () => {
    const onPress = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <IconButton icon={CloseIcon} aria-label="Close" onPress={onPress} />,
    );
    const button = screen.getByRole("button", { name: "Close" });

    await userEvent.tab();
    await expect.element(button).toHaveFocus();
    await expect.element(screen.getByRole("tooltip")).toHaveTextContent("Close");
    await userEvent.keyboard("{Enter}");

    expect(onPress).toHaveBeenCalledOnce();
    await expectNoAxeViolations();
  });

  it("0015 AC-15: requires aria-label in its type (A11Y-3)", () => {
    // @ts-expect-error An icon only button without aria-label doesn't compile.
    const element = <IconButton icon={CloseIcon} />;
    expect(element).toBeDefined();
  });

  it("0015 AC-15: an unavailable icon button ignores presses and keeps focus", async () => {
    const onPress = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <IconButton
        icon={CloseIcon}
        aria-label="Delete"
        onPress={onPress}
        unavailableReason={reason}
      />,
    );

    await userEvent.tab();
    await userEvent.keyboard("{Enter}");

    expect(onPress).not.toHaveBeenCalled();
    await expect.element(screen.getByRole("button", { name: "Delete" })).toHaveFocus();
  });
});
