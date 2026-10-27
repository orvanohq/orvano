import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { z } from "@/shared/zod";
import { expectNoAxeViolations } from "@/test/axe";
import { deferred } from "@/test/deferred";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { useAppForm, type SubmitErrors } from "./index";
import { hasUnsavedChanges, resetUnsavedChangesForTests } from "./unsaved-changes";

// Spec 0015 AC-15, AC-16: the form wiring (spec 0003 FRM-1 to FRM-6) and the UX-8 timing, in three engines.

const schema = z.object({
  name: z.string().trim().min(1, "Enter a bucket name.").max(5, "Use at most 5 characters."),
  region: z.string().min(1, "Choose a region."),
});

type Values = z.input<typeof schema>;

interface FormProps {
  onSubmitAsync?: (props: { value: Values }) => Promise<SubmitErrors<Values> | undefined>;
}

function BucketForm({ onSubmitAsync = () => Promise.resolve(undefined) }: FormProps) {
  const form = useAppForm({ defaultValues: { name: "", region: "" }, schema, onSubmitAsync });
  return (
    <form.AppForm>
      <form.Form>
        <form.ErrorSummary />
        <form.AppField name="name">
          {(field) => <field.TextField label="Bucket name" description="Lowercase." />}
        </form.AppField>
        <form.AppField name="region">{(field) => <field.TextField label="Region" />}</form.AppField>
        <form.SubmitButton>Create bucket</form.SubmitButton>
      </form.Form>
    </form.AppForm>
  );
}

afterEach(resetUnsavedChangesForTests);

describe("useAppForm timing (FRM-2, UX-8)", () => {
  it("0015 AC-16: checks a field only once it loses focus after a change, then on every change", async () => {
    const { screen } = await renderInRouter(<BucketForm />);
    const name = screen.getByRole("textbox", { name: "Bucket name" });

    await focusElement(name.element());
    await userEvent.keyboard("toolong");
    // Still typing: no error yet.
    await expect.element(name).not.toHaveAttribute("aria-invalid");
    await expect.element(screen.getByText("Use at most 5 characters.")).not.toBeInTheDocument();

    await focusElement(screen.getByRole("textbox", { name: "Region" }).element());
    await expect.element(name).toHaveAttribute("aria-invalid", "true");
    await expect.element(name).toHaveAccessibleDescription(/Use at most 5 characters\./);
    await expectNoAxeViolations();

    // Once shown, each change checks it again, and the error clears the moment the value passes.
    await focusElement(name.element());
    await userEvent.keyboard("{Backspace}{Backspace}{Backspace}");
    await expect.element(name).not.toHaveAttribute("aria-invalid");

    // The next mistake waits for the next blur.
    await userEvent.keyboard("xyz");
    await expect.element(name).not.toHaveAttribute("aria-invalid");
  });

  it("0015 AC-16: leaving a field you didn't change shows nothing", async () => {
    const { screen } = await renderInRouter(<BucketForm />);

    await focusElement(screen.getByRole("textbox", { name: "Bucket name" }).element());
    await focusElement(screen.getByRole("textbox", { name: "Region" }).element());

    await expect
      .element(screen.getByRole("textbox", { name: "Bucket name" }))
      .not.toHaveAttribute("aria-invalid");
  });
});

describe("submitting (FRM-4, FRM-6)", () => {
  it("0015 AC-16: a failed submit keeps the values, shows each error, and moves focus to the summary", async () => {
    const onSubmitAsync = vi.fn<FormProps["onSubmitAsync"] & {}>();
    const { screen } = await renderInRouter(<BucketForm onSubmitAsync={onSubmitAsync} />);
    const name = screen.getByRole("textbox", { name: "Bucket name" });

    await focusElement(name.element());
    await userEvent.keyboard("toolong");
    await screen.getByRole("button", { name: "Create bucket" }).click();

    const summary = screen.getByRole("group", { name: "Fix these 2 problems to continue" });
    await expect.element(summary).toHaveFocus();
    await expect.element(name).toHaveValue("toolong");
    await expect.element(name).toHaveAttribute("aria-invalid", "true");
    expect(onSubmitAsync).not.toHaveBeenCalled();
    await expectNoAxeViolations();

    // Each link moves focus to its field.
    await screen.getByRole("link", { name: "Choose a region." }).click();
    await expect.element(screen.getByRole("textbox", { name: "Region" })).toHaveFocus();
  });

  it("0015 AC-16: server errors land at their field and in the summary", async () => {
    const { screen } = await renderInRouter(
      <BucketForm
        onSubmitAsync={() =>
          Promise.resolve({
            form: "The server refused the bucket.",
            fields: { name: "A bucket with this name already exists." },
          })
        }
      />,
    );

    await focusElement(screen.getByRole("textbox", { name: "Bucket name" }).element());
    await userEvent.keyboard("logs");
    await focusElement(screen.getByRole("textbox", { name: "Region" }).element());
    await userEvent.keyboard("eu");
    await screen.getByRole("button", { name: "Create bucket" }).click();

    const summary = screen.getByRole("group", { name: "Fix these 2 problems to continue" });
    await expect.element(summary).toHaveFocus();
    await expect.element(summary.getByText("The server refused the bucket.")).toBeVisible();
    await expect
      .element(screen.getByRole("textbox", { name: "Bucket name" }))
      .toHaveAccessibleDescription(/A bucket with this name already exists\./);
  });

  it("0015 AC-16: shows the busy state while saving and ignores a second press", async () => {
    const saved = deferred<undefined>();
    const onSubmitAsync = vi.fn<FormProps["onSubmitAsync"] & {}>(() => saved.promise);
    const { screen } = await renderInRouter(<BucketForm onSubmitAsync={onSubmitAsync} />);

    await focusElement(screen.getByRole("textbox", { name: "Bucket name" }).element());
    await userEvent.keyboard("logs");
    await focusElement(screen.getByRole("textbox", { name: "Region" }).element());
    await userEvent.keyboard("eu");
    expect(hasUnsavedChanges()).toBe(true);

    const submit = screen.getByRole("button", { name: "Create bucket" });
    await submit.click();
    await expect.element(submit).toHaveAttribute("data-pending", "true");
    await submit.click({ force: true });
    await userEvent.keyboard("{Enter}");
    saved.resolve(undefined);

    await expect.element(submit).not.toHaveAttribute("data-pending");
    expect(onSubmitAsync).toHaveBeenCalledOnce();
    // FRM-5: the saved values become the new defaults, so the form no longer counts as changed.
    await expect.poll(hasUnsavedChanges).toBe(false);
  });
});

describe("unsaved changes (FRM-5)", () => {
  it("0015 AC-16: a form counts as changed only while it differs from its saved values", async () => {
    const { screen } = await renderInRouter(<BucketForm />);
    await focusElement(screen.getByRole("textbox", { name: "Bucket name" }).element());

    await userEvent.keyboard("a");
    await expect.poll(hasUnsavedChanges).toBe(true);
    await userEvent.keyboard("{Backspace}");

    await expect.poll(hasUnsavedChanges).toBe(false);
  });
});
