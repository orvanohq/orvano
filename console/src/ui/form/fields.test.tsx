import { useState } from "react";
import { msg } from "@lingui/core/macro";
import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { z } from "@/shared/zod";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { emulateForcedColors } from "@/test/media";
import { renderInRouter } from "@/test/render";
import { Switch, useAppForm } from "./index";

// Spec 0015 AC-15: each field component's keyboard model and axe, and the error summary reaching every kind of
// control, in three engines.

const regions = [
  { id: "eu", label: "Europe" },
  { id: "us", label: "United States" },
];

const schema = z.object({
  password: z.string(),
  notes: z.string(),
  size: z.optional(z.number().check(z.maximum(10, "Use at most 10 MB."))),
  region: z
    .optional(z.string())
    .check(z.refine((value) => value !== undefined, "Choose a region.")),
  owner: z.optional(z.string()),
  access: z
    .optional(z.string())
    .check(z.refine((value) => value !== undefined, "Choose who can read it.")),
  events: z.array(z.string()),
  encrypted: z.boolean(),
});

function EveryField() {
  const form = useAppForm({
    defaultValues: {
      password: "",
      notes: "",
      size: 1,
      region: undefined as string | undefined,
      owner: undefined as string | undefined,
      access: undefined as string | undefined,
      events: [] as string[],
      encrypted: false,
    },
    schema,
  });
  const [notify, setNotify] = useState(false);
  return (
    <form.AppForm>
      <form.Form>
        <form.ErrorSummary />
        <form.AppField name="password">
          {(field) => <field.PasswordField label="Password" />}
        </form.AppField>
        <form.AppField name="notes">
          {(field) => <field.TextArea label="Notes" isOptional />}
        </form.AppField>
        <form.AppField name="size">{(field) => <field.NumberField label="Size" />}</form.AppField>
        <form.AppField name="region">
          {(field) => <field.Select label="Region" options={regions} />}
        </form.AppField>
        <form.AppField name="owner">
          {(field) => (
            <field.Combobox
              label="Owner"
              options={regions}
              placeholder="Choose an owner"
              isOptional
            />
          )}
        </form.AppField>
        <form.AppField name="access">
          {(field) => (
            <field.RadioGroup
              label="Access"
              options={[
                { id: "private", label: "Private" },
                { id: "public", label: "Public" },
              ]}
            />
          )}
        </form.AppField>
        <form.AppField name="events">
          {(field) => (
            <field.CheckboxGroup
              label="Events"
              options={[
                { id: "created", label: "Created" },
                { id: "deleted", label: "Deleted" },
              ]}
            />
          )}
        </form.AppField>
        <form.AppField name="encrypted">
          {(field) => <field.Checkbox label="Encrypt" />}
        </form.AppField>
        <Switch label="Notify me" isSelected={notify} onChange={setNotify} />
        <form.SubmitButton>Save</form.SubmitButton>
      </form.Form>
    </form.AppForm>
  );
}

/** A control that isn't a field component, framed by `Field`, which can't change. */
function Custom() {
  const form = useAppForm({ defaultValues: { tag: "a" } });
  return (
    <form.AppForm>
      <form.Form>
        <form.AppField name="tag">
          {(field) => (
            <field.Field label="Tag" isDisabled isReadOnly>
              {({ isDisabled, isReadOnly, value, onChange, ...control }) => (
                <input
                  {...control}
                  disabled={isDisabled}
                  readOnly={isReadOnly}
                  value={String(value)}
                  onChange={(event) => {
                    onChange(event.target.value);
                  }}
                />
              )}
            </field.Field>
          )}
        </form.AppField>
      </form.Form>
    </form.AppForm>
  );
}

describe("field components", () => {
  it("0015 PRM-1: a text area is as tall as its rows", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const notes = screen.getByRole("textbox", { name: /Notes/ }).element();

    expect(notes.getBoundingClientRect().height).toBeGreaterThan(70);
  });

  it("0015 AC-15: every field passes axe and marks optional ones", async () => {
    const { screen } = await renderInRouter(<EveryField />);

    await expect.element(screen.getByRole("textbox", { name: "Notes (optional)" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("0015 AC-6: in forced colors every text control keeps a visible border (THM-6)", async ({
    skip,
  }) => {
    await emulateForcedColors(true);
    try {
      // WebKit doesn't emulate forced colors (Safari has no such mode).
      if (!window.matchMedia("(forced-colors: active)").matches) skip();
      const { screen } = await renderInRouter(<EveryField />);

      for (const control of [
        screen.getByLabelText("Password", { exact: true }),
        screen.getByRole("textbox", { name: "Notes (optional)" }),
        screen.getByRole("textbox", { name: "Size" }),
        screen.getByRole("combobox", { name: "Owner (optional)" }),
      ]) {
        // oxlint-disable-next-line no-await-in-loop -- one control at a time
        await expect.element(control).toBeVisible();
        // The border may sit on the control or on the group around it (a number field's buttons, a combobox).
        const bordered = [control.element(), control.element().parentElement].map((element) =>
          element ? getComputedStyle(element) : undefined,
        );
        expect(
          bordered.some(
            (style) =>
              style?.borderTopStyle === "solid" && Number.parseFloat(style.borderTopWidth) >= 1,
          ),
        ).toBe(true);
      }
    } finally {
      await emulateForcedColors(false);
    }
  });

  it("0015 AC-15: the password field shows and hides its value", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const input = screen.getByLabelText("Password", { exact: true });

    await expect.element(input).toHaveAttribute("type", "password");
    await screen.getByRole("button", { name: "Show password" }).click();
    await expect.element(input).toHaveAttribute("type", "text");
    await screen.getByRole("button", { name: "Hide password" }).click();
    await expect.element(input).toHaveAttribute("type", "password");
  });

  it("0015 AC-15: the select opens with the keyboard and picks with Enter", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const trigger = screen.getByRole("button", { name: /Region/ });

    await focusElement(trigger.element());
    await userEvent.keyboard("{ArrowDown}");
    await expect.element(screen.getByRole("option", { name: "Europe" })).toHaveFocus();
    await userEvent.keyboard("{ArrowDown}{Enter}");

    await expect.element(trigger).toHaveTextContent("United States");
    await expect.element(trigger).toHaveFocus();
  });

  it("0015 AC-15: an empty combobox shows its placeholder, like an empty select", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const input = screen.getByRole("combobox", { name: "Owner (optional)" });

    await expect.element(input).toHaveAttribute("placeholder", "Choose an owner");
    await expect.element(input).toHaveValue("");
  });

  it("0015 AC-15: the combobox filters as you type and says when nothing matches", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const input = screen.getByRole("combobox", { name: "Owner (optional)" });

    await focusElement(input.element());
    await userEvent.keyboard("zz");
    await expect.element(screen.getByText("No matches")).toBeVisible();
    await userEvent.keyboard("{Backspace}{Backspace}Eu");
    await expect.element(screen.getByRole("option", { name: "Europe" })).toBeVisible();
    await userEvent.keyboard("{ArrowDown}{Enter}");

    await expect.element(input).toHaveValue("Europe");
  });

  it("0015 AC-15: arrows select a radio, Space toggles checkboxes and the switch", async () => {
    const { screen } = await renderInRouter(<EveryField />);

    await focusElement(screen.getByRole("radio", { name: "Private" }).element());
    await userEvent.keyboard("{ArrowDown}");
    await expect.element(screen.getByRole("radio", { name: "Public" })).toBeChecked();

    await focusElement(screen.getByRole("checkbox", { name: "Encrypt" }).element());
    await userEvent.keyboard(" ");
    await expect.element(screen.getByRole("checkbox", { name: "Encrypt" })).toBeChecked();

    await focusElement(screen.getByRole("switch", { name: "Notify me" }).element());
    await userEvent.keyboard(" ");
    await expect.element(screen.getByRole("switch", { name: "Notify me" })).toBeChecked();
  });

  it("0015 AC-15: arrows step the number field, and its error shows once it loses focus", async () => {
    const { screen } = await renderInRouter(<EveryField />);
    const size = screen.getByRole("textbox", { name: "Size" });

    await focusElement(size.element());
    await userEvent.keyboard("{ArrowUp}");
    await expect.element(size).toHaveValue("2");
    await userEvent.keyboard("{Control>}a{/Control}{Meta>}a{/Meta}25");
    await expect.element(size).not.toHaveAttribute("aria-invalid");

    await focusElement(screen.getByRole("radio", { name: "Private" }).element());
    await expect.element(size).toHaveAttribute("aria-invalid", "true");
  });

  it("0015 AC-16: the summary's links reach a select and a radio group", async () => {
    const { screen } = await renderInRouter(<EveryField />);

    await screen.getByRole("button", { name: "Save" }).click();
    await expect
      .element(screen.getByRole("group", { name: "Fix these 2 problems to continue" }))
      .toHaveFocus();

    // The select's trigger holds focus, so it carries the invalid state.
    await expect
      .element(screen.getByRole("button", { name: /Region/ }))
      .toHaveAttribute("aria-invalid", "true");
    await screen.getByRole("link", { name: "Choose a region." }).click();
    await expect.element(screen.getByRole("button", { name: /Region/ })).toHaveFocus();
    await screen.getByRole("link", { name: "Choose who can read it." }).click();
    await expect.element(screen.getByRole("radio", { name: "Private" })).toHaveFocus();
    await expectNoAxeViolations();
  });

  it("0015 AC-15: a custom field passes isDisabled and isReadOnly on to its control", async () => {
    const { screen } = await renderInRouter(<Custom />);
    const input = screen.getByRole("textbox", { name: "Tag" });

    await expect.element(input).toBeDisabled();
    await expect.element(input).toHaveAttribute("readonly");
  });
});

describe("Switch", () => {
  const reason = msg`You need the Admin role to change this.`;

  function Setting({ onChange }: { onChange: (isSelected: boolean) => void }) {
    return (
      <Switch
        label="Public bucket"
        isSelected={false}
        onChange={onChange}
        unavailableReason={reason}
      />
    );
  }

  it("0015 AC-15: with unavailableReason it keeps focus, ignores Space and presses, and gives its reason", async () => {
    const changes: boolean[] = [];
    const { screen } = await renderInRouter(<Setting onChange={(next) => changes.push(next)} />);
    const toggle = screen.getByRole("switch", { name: "Public bucket" });

    await expect.element(toggle).toHaveAttribute("aria-disabled", "true");
    await expect
      .element(toggle)
      .toHaveAccessibleDescription("You need the Admin role to change this.");
    await expect.element(screen.getByText("You need the Admin role to change this.")).toBeVisible();

    await focusElement(toggle);
    await userEvent.keyboard(" ");
    await expect.element(toggle).toHaveFocus();
    await expect.element(toggle).not.toBeChecked();
    // Playwright won't press an aria-disabled control by itself; force the press to check it is ignored.
    await screen.getByText("Public bucket").click({ force: true });
    await expect.element(toggle).not.toBeChecked();
    expect(changes).toEqual([]);
    await expectNoAxeViolations();
  });
});
