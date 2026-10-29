import { describe, expect, it } from "vitest";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { getMessages, MESSAGE_HISTORY_LIMIT } from "@/shared/stores/messages";
import { notify, ToastRegion, TOAST_TIMEOUT_MS } from ".";

// Spec 0015 AC-12, PRM-8, spec 0001 UX-24: one notify() feeds the toast queue and the message history; React Aria's
// toast region shows and announces the toasts. In three engines.

async function renderRegion() {
  return renderInRouter(
    <>
      <label>
        Table name
        <input />
      </label>
      <ToastRegion />
    </>,
  );
}

/** Each visible toast's title (its accessible name), top to bottom. */
const toastTitles = () =>
  Array.from(document.querySelectorAll("[role='alertdialog']")).map(
    (toast) => document.getElementById(toast.getAttribute("aria-labelledby") ?? "")?.textContent,
  );

/** The top visible toast. */
const topToast = () => document.querySelector("[role='alertdialog']");

describe("notify and Toast", () => {
  it("0015 AC-12: shows at most three, newest on top, and keeps every message in the history", async () => {
    const { screen } = await renderRegion();
    for (const name of ["users", "orders", "invoices", "events"]) {
      notify({ tone: "success", message: `Table ${name} created`, source: "Databases" });
    }

    const region = screen.getByRole("region", { name: "Notifications" });
    await expect.element(region).toBeVisible();
    await expect
      .poll(toastTitles)
      .toEqual(["Table events created", "Table invoices created", "Table orders created"]);
    expect(getMessages().map((message) => message.message)).toEqual([
      "Table events created",
      "Table invoices created",
      "Table orders created",
      "Table users created",
    ]);
    await expectNoAxeViolations();
  });

  it("0015 AC-12: merges identical toasts with a count, and the history keeps both", async () => {
    const { screen } = await renderRegion();
    notify({ tone: "error", message: "Deploy failed", source: "Functions" });
    notify({ tone: "error", message: "Deploy failed", source: "Functions" });

    await expect.element(screen.getByText("Functions, 2 times")).toBeVisible();
    expect(toastTitles()).toEqual(["Deploy failed"]);
    expect(getMessages()).toHaveLength(2);
  });

  it("0001 UX-24: a repeated toast restarts its timer and is announced again", async () => {
    const { screen } = await renderRegion();
    notify({ tone: "success", message: "Saved", source: "Settings" });
    await expect.poll(toastTitles).toEqual(["Saved"]);
    const first = topToast();

    await new Promise((resolve) => setTimeout(resolve, TOAST_TIMEOUT_MS / 2));
    notify({ tone: "success", message: "Saved", source: "Settings" });

    // A new toast element replaces the first, so the region announces it again; the count carries over.
    await expect.element(screen.getByText("Settings, 2 times")).toBeVisible();
    expect(topToast()).not.toBe(first);
    expect(toastTitles()).toEqual(["Saved"]);
    // Past the first toast's 6 s, the repeat is still showing: its timer started over.
    await new Promise((resolve) => setTimeout(resolve, TOAST_TIMEOUT_MS / 2 + 1000));
    expect(toastTitles()).toEqual(["Saved"]);
    await expect.poll(toastTitles, { timeout: TOAST_TIMEOUT_MS, interval: 250 }).toEqual([]);
  });

  it("0015 AC-12: never takes focus from the field the user is in", async () => {
    const { screen } = await renderRegion();
    const field = screen.getByRole("textbox", { name: "Table name" });
    await focusElement(field);

    notify({ tone: "success", message: "Saved", source: "Settings" });

    await expect.element(screen.getByText("Saved")).toBeVisible();
    await expect.element(field).toHaveFocus();
  });

  it("0015 AC-12: a success toast closes after 6 s; an error stays until dismissed", async () => {
    const { screen } = await renderRegion();
    notify({ tone: "success", message: "Saved", source: "Settings" });
    notify({ tone: "error", message: "Deploy failed", source: "Functions" });
    await expect.poll(toastTitles).toEqual(["Deploy failed", "Saved"]);

    await expect
      .poll(toastTitles, { timeout: TOAST_TIMEOUT_MS + 2000, interval: 250 })
      .toEqual(["Deploy failed"]);

    await screen.getByRole("button", { name: "Dismiss" }).click();
    await expect.poll(toastTitles).toEqual([]);
  });

  it("0001 UX-24: the history keeps the last 50 messages", () => {
    for (let index = 0; index < MESSAGE_HISTORY_LIMIT + 5; index++) {
      notify({ tone: "info", message: `Message ${String(index)}`, source: "Test" });
    }
    const messages = getMessages();
    expect(messages).toHaveLength(MESSAGE_HISTORY_LIMIT);
    expect(messages[0]?.message).toBe(`Message ${String(MESSAGE_HISTORY_LIMIT + 4)}`);
  });
});
