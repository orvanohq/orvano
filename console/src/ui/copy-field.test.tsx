import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { CodeSnippet } from "./code-snippet";
import { CopyField } from "./copy-field";
import { SecretReveal } from "./secret-reveal";

// Spec 0015 AC-24, PRM-7, PRM-9, spec 0001 UX-33: copying through the clipboard, the fallback when it is missing or
// refuses, and a secret shown once, in three engines.

/** The text in React Aria's live announcer (the one region every announcement goes through, UX-41). */
const announced = () =>
  Array.from(document.querySelectorAll("[aria-live]"))
    .map((node) => node.textContent)
    .join(" ");

/** The text of the elements an element's `aria-describedby` points at. */
const description = (element: Element) =>
  (element.getAttribute("aria-describedby") ?? "")
    .split(" ")
    .map((id) => document.getElementById(id)?.textContent ?? "")
    .join(" ")
    .trim();

function stubClipboard(writeText: (text: string) => Promise<void>) {
  const spy = vi.fn<(text: string) => Promise<void>>(writeText);
  vi.spyOn(navigator, "clipboard", "get").mockReturnValue({
    writeText: spy,
  } as unknown as Clipboard);
  return spy;
}

/** A sheet that creates a second key without closing: the same `SecretReveal` gets a new `secret`. */
function RotatingSecret() {
  const [key, setKey] = useState({ secret: "ok_live_first_aaaa", masked: "ok_live_…aaaa" });
  return (
    <>
      <SecretReveal label="API key" {...key} />
      <button
        type="button"
        onClick={() => {
          setKey({ secret: "ok_live_second_bbbb", masked: "ok_live_…bbbb" });
        }}
      >
        Rotate
      </button>
    </>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("CopyField", () => {
  it("0015 AC-24: copies through the clipboard and announces Copied", async () => {
    const writeText = stubClipboard(() => Promise.resolve());
    const { screen } = await renderInRouter(
      <CopyField label="Endpoint" value="https://api.example.com/v1" />,
    );

    await expect
      .element(screen.getByRole("textbox", { name: "Endpoint" }))
      .toHaveValue("https://api.example.com/v1");
    const copy = screen.getByRole("button", { name: "Copy Endpoint" });
    await focusElement(copy);
    await userEvent.keyboard("{Enter}");

    expect(writeText).toHaveBeenCalledWith("https://api.example.com/v1");
    await expect.poll(announced).toContain("Copied");
    await expectNoAxeViolations();
  });

  it("0015 AC-24: when the clipboard refuses, selects the value, shows the keyboard hint, and claims nothing", async () => {
    stubClipboard(() => Promise.reject(new DOMException("Denied", "NotAllowedError")));
    const { screen } = await renderInRouter(
      <CopyField label="Endpoint" value="https://api.example.com/v1" />,
    );
    const field = screen.getByRole("textbox", { name: "Endpoint" });

    await screen.getByRole("button", { name: "Copy Endpoint" }).click();

    await expect.element(screen.getByText(/^Press (Ctrl\+C|⌘C) to copy$/)).toBeVisible();
    await expect.element(field).toHaveFocus();
    const input = field.element() as HTMLInputElement;
    expect(input.value.slice(input.selectionStart ?? 0, input.selectionEnd ?? 0)).toBe(
      "https://api.example.com/v1",
    );
    expect(description(field.element())).toMatch(/to copy$/);
    expect(announced()).not.toContain("Copied");
    await expectNoAxeViolations();
  });

  it("0015 AC-24: without a clipboard at all (not a secure context), takes the same fallback", async () => {
    vi.spyOn(navigator, "clipboard", "get").mockReturnValue(undefined as unknown as Clipboard);
    const { screen } = await renderInRouter(<CopyField label="Project ID" value="prj_1" />);

    await screen.getByRole("button", { name: "Copy Project ID" }).click();

    await expect.element(screen.getByText(/to copy$/)).toBeVisible();
    expect(announced()).not.toContain("Copied");
  });
});

describe("SecretReveal", () => {
  it("0015 PRM-7: shows the secret once, then only the masked value, with no copy button", async () => {
    stubClipboard(() => Promise.resolve());
    const onDone = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <SecretReveal
        label="API key"
        secret="ok_live_secret_91c2"
        masked="ok_live_…91c2"
        onDone={onDone}
      />,
    );

    await expect
      .poll(() => screen.getByRole("status").element().textContent)
      .toContain("Copy this secret now");
    await expect
      .element(screen.getByRole("textbox", { name: "API key" }))
      .toHaveValue("ok_live_secret_91c2");
    await expectNoAxeViolations();

    await screen.getByRole("button", { name: "I've copied it" }).click();

    await expect.element(screen.getByText("ok_live_…91c2")).toBeVisible();
    expect(document.body.textContent).not.toContain("ok_live_secret_91c2");
    expect(document.querySelector("input")).toBeNull();
    expect(screen.getByRole("button", { name: "Copy API key" }).elements()).toHaveLength(0);
    expect(onDone).toHaveBeenCalledOnce();
    // Focus moves to what replaced the button, not to the page.
    await expect.poll(() => document.activeElement?.textContent).toContain("ok_live_…91c2");
  });

  it("0015 PRM-7: a new secret while mounted shows the new one, not the old one or its mask", async () => {
    stubClipboard(() => Promise.resolve());
    const { screen } = await renderInRouter(<RotatingSecret />);
    const field = screen.getByRole("textbox", { name: "API key" });

    // Rotating before "I've copied it" swaps the value shown.
    await expect.element(field).toHaveValue("ok_live_first_aaaa");
    await screen.getByRole("button", { name: "Rotate" }).click();
    await expect.element(field).toHaveValue("ok_live_second_bbbb");
  });

  it("0015 PRM-7: a new secret after I've copied it shows the new secret again", async () => {
    stubClipboard(() => Promise.resolve());
    const { screen } = await renderInRouter(<RotatingSecret />);

    await screen.getByRole("button", { name: "I've copied it" }).click();
    await expect.element(screen.getByText("ok_live_…aaaa")).toBeVisible();
    await screen.getByRole("button", { name: "Rotate" }).click();

    await expect
      .element(screen.getByRole("textbox", { name: "API key" }))
      .toHaveValue("ok_live_second_bbbb");
    expect(document.body.textContent).not.toContain("ok_live_…aaaa");
    expect(document.body.textContent).not.toContain("ok_live_first_aaaa");
  });
});

describe("CodeSnippet", () => {
  it("0015 AC-24: copies the code, and without the clipboard selects it and shows the hint", async () => {
    const writeText = stubClipboard(() => Promise.reject(new Error("refused")));
    const { screen } = await renderInRouter(
      <CodeSnippet label="Install command" code="npm install @orvano/sdk" />,
    );

    await screen.getByRole("button", { name: "Copy Install command" }).click();

    expect(writeText).toHaveBeenCalledWith("npm install @orvano/sdk");
    await expect.element(screen.getByText(/to copy$/)).toBeVisible();
    expect(window.getSelection()?.toString()).toBe("npm install @orvano/sdk");
    expect(announced()).not.toContain("Copied");
    await expectNoAxeViolations();
  });

  it("0015 AC-24: the code is a named region, described and announced by the hint when the copy falls back", async () => {
    stubClipboard(() => Promise.reject(new Error("refused")));
    const { screen } = await renderInRouter(
      <CodeSnippet label="Install command" code="npm install @orvano/sdk" />,
    );
    const region = screen.getByRole("region", { name: "Install command" });
    await expect.element(region).toHaveAttribute("tabindex", "0");
    expect(region.element().hasAttribute("aria-describedby")).toBe(false);

    await screen.getByRole("button", { name: "Copy Install command" }).click();

    await expect.poll(() => description(region.element())).toMatch(/to copy$/);
    await expect.poll(announced).toMatch(/to copy/);
  });
});
