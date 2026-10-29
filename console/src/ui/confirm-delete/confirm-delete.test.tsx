import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { deferred } from "@/test/deferred";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import {
  ConfirmDelete,
  tiers,
  type ConfirmDeleteProps,
  type Dependent,
  type LoadDependents,
  type ResourceKind,
} from "./index";

// Spec 0015 AC-17, spec 0001 UX-23: the tiers, what goes with it, and its states, in three engines.

type Scenario = ConfirmDeleteProps extends infer Props
  ? Props extends ConfirmDeleteProps
    ? Omit<Props, "isOpen" | "onOpenChange">
    : never
  : never;

function Harness(props: Scenario) {
  const [isOpen, setOpen] = useState(true);
  return (
    <ConfirmDelete {...(props as ConfirmDeleteProps)} isOpen={isOpen} onOpenChange={setOpen} />
  );
}

const noop = () => Promise.resolve();

describe("tiers", () => {
  it("0015 AC-17: maps every UX-23 kind, tier 1 for the resources that hold data", () => {
    // @ts-expect-error -- a kind missing from the map fails the type check (PRM-6).
    const missing: Record<ResourceKind, 1 | 2> = { organization: 1 };
    expect(missing).toBeDefined();

    expect(
      Object.entries(tiers)
        .filter(([, tier]) => tier === 1)
        .map(([kind]) => kind),
    ).toEqual(["organization", "project", "database", "bucket"]);
  });
});

describe("ConfirmDelete", () => {
  it("0015 AC-17: tier 1 keeps Delete unavailable until the exact name is typed", async () => {
    const onConfirm = vi.fn<() => Promise<void>>(noop);
    const { screen } = await renderInRouter(
      <Harness kind="project" name="storefront" onConfirm={onConfirm} />,
    );
    const remove = screen.getByRole("button", { name: "Delete project" });

    await expect
      .element(screen.getByRole("alertdialog", { name: "Delete storefront?" }))
      .toBeVisible();
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await expect.element(remove).toHaveAccessibleDescription("Type the name to confirm.");
    await expectNoAxeViolations();
    await remove.click({ force: true });
    expect(onConfirm).not.toHaveBeenCalled();

    await screen.getByRole("textbox", { name: "Type storefront to confirm" }).fill("storefron");
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await screen.getByRole("textbox", { name: "Type storefront to confirm" }).fill("storefront");
    await expect.element(remove).not.toHaveAttribute("aria-disabled");
    await remove.click();

    expect(onConfirm).toHaveBeenCalledOnce();
    await expect.element(screen.getByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("0015 AC-17: the alert dialog is described by what it deletes (R-7, SRT-6)", async () => {
    const { screen } = await renderInRouter(
      <Harness kind="project" name="storefront" onConfirm={noop} />,
    );

    await expect
      .element(screen.getByRole("alertdialog", { name: "Delete storefront?" }))
      .toHaveAccessibleDescription(
        "This deletes storefront and everything in it. You can't undo this.",
      );
    await expectNoAxeViolations();
  });

  it("0015 AC-17: tier 2 starts on Cancel and names the button by kind", async () => {
    const { screen } = await renderInRouter(<Harness kind="table" name="users" onConfirm={noop} />);

    await expect.element(screen.getByRole("button", { name: "Cancel" })).toHaveFocus();
    await expect.element(screen.getByRole("button", { name: "Delete table" })).toBeVisible();
    await userEvent.keyboard("{Escape}");
    await expect.element(screen.getByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("0015 AC-17: shows loading, then what goes with it, labeling estimates 'about'", async () => {
    const counted = deferred<{ items: Dependent[] }>();
    const loadDependents = vi.fn<LoadDependents>(() => counted.promise);
    const { screen } = await renderInRouter(
      <Harness kind="database" name="main" onConfirm={noop} loadDependents={loadDependents} />,
    );

    await expect.element(screen.getByText("Counting what will be deleted with it…")).toBeVisible();
    expect(loadDependents.mock.calls[0]?.[0]).toBeInstanceOf(AbortSignal);
    counted.resolve({
      items: [
        { kind: "table", count: 12, estimated: false },
        { kind: "row", count: 48_210, estimated: true },
        { kind: "index", count: 0, estimated: false },
      ],
    });

    await expect.element(screen.getByText("12 tables")).toBeVisible();
    await expect.element(screen.getByText("about 48,210 rows")).toBeVisible();
    await expect.element(screen.getByText(/indexes/)).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("0015 AC-17: says when counting failed and still lets you delete", async () => {
    const onConfirm = vi.fn<() => Promise<void>>(noop);
    const { screen } = await renderInRouter(
      <Harness
        kind="function"
        name="resize"
        onConfirm={onConfirm}
        loadDependents={() => Promise.reject(new Error("down"))}
      />,
    );

    await expect
      .element(screen.getByText("Couldn't count what will be deleted. You can still delete it."))
      .toBeVisible();
    await screen.getByRole("button", { name: "Delete function" }).click();
    expect(onConfirm).toHaveBeenCalledOnce();
  });

  it("0015 AC-17: a new loadDependents on a later render doesn't load again (R-10)", async () => {
    const calls = vi.fn<LoadDependents>(() =>
      Promise.resolve({ items: [{ kind: "table", count: 2, estimated: false }] }),
    );
    function Parent() {
      const [renders, setRenders] = useState(0);
      return (
        <>
          <button
            type="button"
            onClick={() => {
              setRenders(renders + 1);
            }}
          >
            Rendered {renders}
          </button>
          <Harness
            kind="database"
            name="main"
            onConfirm={noop}
            // A new function each render, as a caller that builds it inline would pass.
            loadDependents={(signal) => calls(signal)}
          />
        </>
      );
    }
    const { screen } = await renderInRouter(<Parent />);
    await expect.element(screen.getByText("2 tables")).toBeVisible();

    // The modal hides the rest of the page from pointers, so the parent renders again from code.
    const again = screen.getByText(/^Rendered/).element() as HTMLButtonElement;
    again.click();
    await expect.poll(() => again.textContent).toBe("Rendered 1");
    await expect.element(screen.getByText("2 tables")).toBeVisible();
    expect(calls).toHaveBeenCalledOnce();
  });

  it("0015 AC-17: aborts the count when it closes", async () => {
    let signal: AbortSignal | undefined;
    const { screen } = await renderInRouter(
      <Harness
        kind="table"
        name="users"
        onConfirm={noop}
        loadDependents={(given) => {
          signal = given;
          return new Promise(() => undefined);
        }}
      />,
    );

    await screen.getByRole("button", { name: "Cancel" }).click();
    await expect.element(screen.getByRole("alertdialog")).not.toBeInTheDocument();
    expect(signal?.aborted).toBe(true);
  });

  it("0015 AC-17: a failed delete stays open with an inline error", async () => {
    const { screen } = await renderInRouter(
      <Harness kind="webhook" name="deploys" onConfirm={() => Promise.reject(new Error("500"))} />,
    );

    await screen.getByRole("button", { name: "Delete webhook" }).click();
    const alert = screen.getByRole("alertdialog").getByRole("alert");
    await expect
      .poll(() => alert.element().textContent)
      .toBe("Couldn't deleteTry again in a moment.");
    await expect.element(screen.getByRole("alertdialog")).toBeVisible();
  });

  it("0001 UX-22: a failed delete's kind picks the words (R-22)", async () => {
    const { screen } = await renderInRouter(
      <Harness
        kind="webhook"
        name="deploys"
        onConfirm={() =>
          Promise.reject(
            Object.assign(new Error("403"), {
              kind: "forbidden",
              requiredRole: "Admin",
              requestId: "req_123",
            }),
          )
        }
      />,
    );

    await screen.getByRole("button", { name: "Delete webhook" }).click();
    const alert = screen.getByRole("alertdialog").getByRole("alert");
    // The kind picks the words, not the network advice (UX-22), and the request ID shows.
    await expect
      .poll(() => alert.element().textContent)
      .toBe("You don't have accessYou need the Admin role for this.Request ID: req_123");
    await expectNoAxeViolations();
  });

  it("0001 UX-23: Escape and Cancel can't close it while the delete runs, so a later failure shows", async () => {
    let fail: ((error: Error) => void) | undefined;
    const deleting = new Promise<void>((_, reject) => {
      fail = reject;
    });
    const { screen } = await renderInRouter(
      <Harness kind="webhook" name="deploys" onConfirm={() => deleting} />,
    );
    const remove = screen.getByRole("button", { name: "Delete webhook" });

    await remove.click();
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await expect.element(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    await focusElement(remove);
    await userEvent.keyboard("{Escape}");
    await expect
      .element(screen.getByRole("alertdialog", { name: "Delete deploys?" }))
      .toBeVisible();

    fail?.(new Error("500"));
    await expect
      .poll(() => screen.getByRole("alertdialog").getByRole("alert").element().textContent)
      .toBe("Couldn't deleteTry again in a moment.");
    await expect.element(screen.getByRole("button", { name: "Cancel" })).toBeEnabled();
    await userEvent.keyboard("{Escape}");
    await expect.element(screen.getByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("0001 UX-23: a delete that succeeds after an Escape still closes it once", async () => {
    const deleting = deferred<undefined>();
    const onOpenChange = vi.fn<(isOpen: boolean) => void>();
    const { screen } = await renderInRouter(
      <ConfirmDelete
        kind="table"
        name="users"
        isOpen
        onOpenChange={onOpenChange}
        onConfirm={() => deleting.promise}
      />,
    );
    const remove = screen.getByRole("button", { name: "Delete table" });

    await remove.click();
    await focusElement(remove);
    await userEvent.keyboard("{Escape}");
    expect(onOpenChange).not.toHaveBeenCalled();

    deleting.resolve(undefined);
    await expect.poll(() => onOpenChange.mock.calls).toEqual([[false]]);
  });

  it("0015 AC-17: a single row offers Don't ask again; a bulk delete over 1,000 asks for the count", async () => {
    const onDontAskAgainChange = vi.fn<(value: boolean) => void>();
    const first = await renderInRouter(
      <Harness
        kind="row"
        dontAskAgain={false}
        onDontAskAgainChange={onDontAskAgainChange}
        onConfirm={noop}
      />,
    );
    await first.screen.getByText("Don't ask again for this table this session").click();
    await expect
      .element(
        first.screen.getByRole("checkbox", { name: "Don't ask again for this table this session" }),
      )
      .toBeInTheDocument();
    expect(onDontAskAgainChange).toHaveBeenCalledWith(true);
    await expect.element(first.screen.getByRole("button", { name: "Delete row" })).toBeVisible();
    await first.screen.unmount();

    const { screen } = await renderInRouter(<Harness kind="rows" count={1200} onConfirm={noop} />);
    const remove = screen.getByRole("button", { name: "Delete 1,200 rows" });
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await screen
      .getByRole("textbox", { name: "Type 1200 to confirm deleting the rows" })
      .fill("1,200");
    await expect.element(remove).not.toHaveAttribute("aria-disabled");
  });

  it("0001 UX-23: the typed count must match as a whole, in the locale's digits too, grouped or not (R-22)", async () => {
    const { screen } = await renderInRouter(<Harness kind="rows" count={1001} onConfirm={noop} />);
    const remove = screen.getByRole("button", { name: "Delete 1,001 rows" });
    const field = screen.getByRole("textbox", { name: "Type 1001 to confirm deleting the rows" });

    await field.fill("1x0x0x1");
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await field.fill("10011");
    await expect.element(remove).toHaveAttribute("aria-disabled", "true");
    await field.fill("١٠٠١");
    await expect.element(remove).not.toHaveAttribute("aria-disabled");
    await field.fill("۱۰۰۱");
    await expect.element(remove).not.toHaveAttribute("aria-disabled");
    await field.fill(" 1,001 ");
    await expect.element(remove).not.toHaveAttribute("aria-disabled");
  });
});
