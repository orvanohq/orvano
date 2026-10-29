import { useLocation } from "@tanstack/react-router";
import { afterEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { isInteractionLocked } from "@/shared/stores/interaction-lock";
import { z } from "@/shared/zod";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { Link } from "../link";
import { SideSheet } from "../side-sheet";
import { useAppForm } from "./index";
import { resetUnsavedChangesForTests, stableKey } from "./unsaved-changes";

// Spec 0015 AC-16, spec 0003 RTE-8: the one guard asks once, with the design system dialog, before a navigation
// throws away changes, and holds the interaction lock (QRY-4) while any form has changes.

afterEach(resetUnsavedChangesForTests);

function NameForm({ label }: { label: string }) {
  const form = useAppForm({ defaultValues: { name: "" }, schema: z.object({ name: z.string() }) });
  return (
    <form.AppForm>
      <form.Form>
        <form.AppField name="name">{(field) => <field.TextField label={label} />}</form.AppField>
      </form.Form>
    </form.AppForm>
  );
}

function Page() {
  return (
    <>
      <NameForm label="Project name" />
      <NameForm label="Display name" />
      <Link to={"/elsewhere" as "/"}>Leave</Link>
      <Link to="/" search={{ tab: "general" } as never}>
        Same page
      </Link>
    </>
  );
}

const elsewhere = [{ path: "/elsewhere" }];

async function type(element: HTMLElement, text: string) {
  await focusElement(element);
  await userEvent.keyboard(text);
}

describe("UnsavedChangesGuard", () => {
  it("0015 AC-16: asks once for several changed forms; Keep editing stays, Discard leaves", async () => {
    const { screen, router } = await renderInRouter(<Page />, elsewhere);

    await type(screen.getByRole("textbox", { name: "Project name" }).element() as HTMLElement, "a");
    await type(screen.getByRole("textbox", { name: "Display name" }).element() as HTMLElement, "b");
    await expect.poll(isInteractionLocked).toBe(true);

    await screen.getByRole("link", { name: "Leave" }).click();
    const question = screen.getByRole("alertdialog", { name: "Discard unsaved changes?" });
    await expect.element(question).toBeVisible();
    expect(document.querySelectorAll("[role=alertdialog]")).toHaveLength(1);
    await expectNoAxeViolations();

    await screen.getByRole("button", { name: "Keep editing" }).click();
    await expect.element(question).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/");
    await expect.element(screen.getByRole("textbox", { name: "Project name" })).toHaveValue("a");

    await screen.getByRole("link", { name: "Leave" }).click();
    await screen.getByRole("button", { name: "Discard changes" }).click();
    await expect.poll(() => router.state.location.pathname).toBe("/elsewhere");
    await expect.poll(isInteractionLocked).toBe(false);
  });

  it("0015 AC-16: lets a navigation through when nothing changed, or when the forms stay mounted", async () => {
    const { screen, router } = await renderInRouter(<Page />, elsewhere);

    await type(screen.getByRole("textbox", { name: "Project name" }).element() as HTMLElement, "a");
    await screen.getByRole("link", { name: "Same page" }).click();
    await expect.poll(() => router.state.location.search as unknown).toEqual({ tab: "general" });
    expect(document.querySelector("[role=alertdialog]")).toBeNull();

    await type(
      screen.getByRole("textbox", { name: "Project name" }).element() as HTMLElement,
      "{Backspace}",
    );
    await screen.getByRole("link", { name: "Leave" }).click();
    await expect.poll(() => router.state.location.pathname).toBe("/elsewhere");
    expect(document.querySelector("[role=alertdialog]")).toBeNull();
  });

  it("0015 AC-16: asks when a changed sheet's search param would close it (RTE-8)", async () => {
    const { screen, router } = await renderInRouter(<SheetPage />);
    // History pushes stand in for the screen's view hook, which owns writing search params (URL-3).
    router.history.push("/?sheet=new");
    await expect.element(screen.getByRole("textbox", { name: "Bucket name" })).toBeVisible();

    await type(
      screen.getByRole("textbox", { name: "Bucket name" }).element() as HTMLElement,
      "logs",
    );
    router.history.push("/?sheet=new&tab=advanced");
    await expect
      .poll(() => router.state.location.search as unknown)
      .toEqual({
        sheet: "new",
        tab: "advanced",
      });
    expect(document.querySelector("[role=alertdialog]")).toBeNull();

    router.history.push("/");
    await expect
      .element(screen.getByRole("alertdialog", { name: "Discard unsaved changes?" }))
      .toBeVisible();
    await screen.getByRole("button", { name: "Keep editing" }).click();
    expect(router.state.location.search as unknown).toEqual({ sheet: "new", tab: "advanced" });
  });
});

function SheetPage() {
  const search = useLocation().search as { sheet?: string };
  const form = useAppForm({ defaultValues: { name: "" }, schema: z.object({ name: z.string() }) });
  return (
    <form.AppForm>
      <SideSheet title="New bucket" isOpen={search.sheet === "new"} onOpenChange={() => undefined}>
        <form.Form>
          <form.AppField name="name">
            {(field) => <field.TextField label="Bucket name" />}
          </form.AppField>
        </form.Form>
      </SideSheet>
    </form.AppForm>
  );
}

describe("watched search params", () => {
  it("0003 RTE-8: an object or array param compares by what it holds, not by identity or key order", () => {
    const filter = { column: "name", op: "eq", values: ["a", { b: 1, c: 2 }] };
    const same = { values: ["a", { c: 2, b: 1 }], op: "eq", column: "name" };
    expect(stableKey(filter)).toBe(stableKey(same));
    expect(stableKey(filter)).not.toBe(stableKey({ ...filter, op: "ne" }));
    expect(stableKey(["a", "b"])).not.toBe(stableKey(["b", "a"]));
    expect(stableKey("row_1")).toBe(stableKey("row_1"));
  });
});
