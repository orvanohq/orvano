import { useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { z } from "@/shared/zod";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { Button } from "./button";
import { Dialog, DialogTrigger } from "./dialog";
import { useAppForm } from "./form";
import { resetUnsavedChangesForTests } from "./form/unsaved-changes";
import { SideSheet } from "./side-sheet";

// Spec 0015 AC-15, AC-16: Dialog and the modal SideSheet (APG dialog: focus in, Tab trapped, Escape closes, focus
// returns), and the sheet asking before it throws away changes (UX-6, UX-9), in three engines.

afterEach(resetUnsavedChangesForTests);

describe("Dialog", () => {
  it("0015 AC-15: opens from its button, keeps Tab inside, and Escape returns focus (SRT-6)", async () => {
    const { screen } = await renderInRouter(
      <DialogTrigger trigger={<Button>Rename table</Button>}>
        <Dialog
          title="Rename table"
          footer={
            <>
              <Button slot="close">Cancel</Button>
              <Button variant="primary">Rename</Button>
            </>
          }
        >
          <p>Pick a new name.</p>
        </Dialog>
      </DialogTrigger>,
    );
    const opener = screen.getByRole("button", { name: "Rename table" });

    await focusElement(opener.element());
    await userEvent.keyboard("{Enter}");
    const dialog = screen.getByRole("dialog", { name: "Rename table" });
    await expect.element(dialog).toBeVisible();
    await expect.poll(() => dialog.element().contains(document.activeElement)).toBe(true);
    await expectNoAxeViolations();

    for (let index = 0; index < 4; index++) {
      // oxlint-disable-next-line no-await-in-loop -- each Tab depends on where the last one put focus
      await userEvent.tab();
      expect(dialog.element().contains(document.activeElement)).toBe(true);
    }

    await userEvent.keyboard("{Escape}");
    await expect.element(dialog).not.toBeInTheDocument();
    await expect.element(opener).toHaveFocus();
  });

  it("0015 AC-15: Cancel closes it", async () => {
    const { screen } = await renderInRouter(
      <DialogTrigger trigger={<Button>Open</Button>}>
        <Dialog title="Confirm" role="alertdialog" footer={<Button slot="close">Cancel</Button>}>
          <p>Sure?</p>
        </Dialog>
      </DialogTrigger>,
    );

    await screen.getByRole("button", { name: "Open" }).click();
    await expect.element(screen.getByRole("alertdialog", { name: "Confirm" })).toBeVisible();
    await screen.getByRole("button", { name: "Cancel" }).click();

    await expect.element(screen.getByRole("alertdialog")).not.toBeInTheDocument();
  });
});

function WebhookSheet() {
  const [isOpen, setOpen] = useState(false);
  const form = useAppForm({
    defaultValues: { url: "" },
    schema: z.object({ url: z.string() }),
  });
  return (
    <>
      <Button
        onPress={() => {
          setOpen(true);
        }}
      >
        New webhook
      </Button>
      <form.AppForm>
        <SideSheet
          title="New webhook"
          isOpen={isOpen}
          onOpenChange={setOpen}
          footer={<Button slot="close">Cancel</Button>}
        >
          <form.Form>
            <form.AppField name="url">{(field) => <field.TextField label="URL" />}</form.AppField>
          </form.Form>
        </SideSheet>
      </form.AppForm>
    </>
  );
}

describe("SideSheet", () => {
  it("0015 AC-15: opens as a labeled modal and Escape closes it when nothing changed", async () => {
    const { screen } = await renderInRouter(<WebhookSheet />);
    const opener = screen.getByRole("button", { name: "New webhook" });

    await focusElement(opener.element());
    await userEvent.keyboard("{Enter}");
    const sheet = screen.getByRole("dialog", { name: "New webhook" });
    await expect.element(sheet).toBeVisible();
    await expectNoAxeViolations();
    await expect.poll(() => sheet.element().contains(document.activeElement)).toBe(true);

    await userEvent.keyboard("{Escape}");
    await expect.element(sheet).not.toBeInTheDocument();
    await expect.element(opener).toHaveFocus();
  });

  it("0015 AC-16: with changes, Escape asks first; Keep editing returns to the sheet (UX-9)", async () => {
    const { screen } = await renderInRouter(<WebhookSheet />);

    await screen.getByRole("button", { name: "New webhook" }).click();
    const url = screen.getByRole("textbox", { name: "URL" });
    await focusElement(url.element());
    await userEvent.keyboard("https://example.com");
    await userEvent.keyboard("{Escape}");

    const question = screen.getByRole("alertdialog", { name: "Discard unsaved changes?" });
    await expect.element(question).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Keep editing" })).toHaveFocus();
    await expectNoAxeViolations();
    await screen.getByRole("button", { name: "Keep editing" }).click();

    await expect.element(question).not.toBeInTheDocument();
    await expect.element(url).toHaveValue("https://example.com");
    await expect
      .poll(() =>
        screen
          .getByRole("dialog", { name: "New webhook" })
          .element()
          .contains(document.activeElement),
      )
      .toBe(true);

    // Cancel asks too; discarding closes the sheet.
    await screen.getByRole("button", { name: "Cancel" }).click();
    await screen.getByRole("button", { name: "Discard changes" }).click();
    await expect
      .element(screen.getByRole("dialog", { name: "New webhook" }))
      .not.toBeInTheDocument();
  });
});
