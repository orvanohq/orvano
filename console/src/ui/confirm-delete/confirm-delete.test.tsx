import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { deferred } from "@/test/deferred";
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
    await expect
      .element(screen.getByText("Couldn't delete. Check your connection and try again."))
      .toBeVisible();
    await expect.element(screen.getByRole("alertdialog")).toBeVisible();
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
});
