import { msg } from "@lingui/core/macro";
import { describe, expect, it } from "vitest";
import { render } from "vitest-browser-react";
import { I18nProvider } from "@lingui/react";
import { activateLocale, i18n } from "@/shared/i18n";
import { expectNoAxeViolations } from "@/test/axe";
import { ariaKeyshortcuts, Kbd } from "./kbd";
import { isApplePlatform } from "./platform";
import { matchesShortcut, useShortcut } from "./use-shortcut";

const apple = isApplePlatform();

function key(init: KeyboardEventInit) {
  return new KeyboardEvent("keydown", init);
}

let triggered = 0;

/** A control bound to the `?` shortcut, counting how often it fires. */
function Bound() {
  const props = useShortcut({
    id: "test-help",
    keys: "?",
    label: msg`Show shortcuts`,
    onTrigger: () => {
      triggered += 1;
    },
  });
  return (
    <button type="button" {...props}>
      Shortcuts
    </button>
  );
}

describe("Kbd and shortcut matching", () => {
  it("0015 PRM-11: Mod is Meta on Apple platforms and Control elsewhere", () => {
    expect(ariaKeyshortcuts("Mod+K")).toBe(apple ? "Meta+K" : "Control+K");
    expect(ariaKeyshortcuts("?")).toBe("?");
  });

  it("0015 PRM-11: matches Mod+K only with the platform's modifier", () => {
    const mod = apple ? { metaKey: true } : { ctrlKey: true };
    const other = apple ? { ctrlKey: true } : { metaKey: true };
    expect(matchesShortcut(key({ key: "k", ...mod }), "Mod+K")).toBe(true);
    expect(matchesShortcut(key({ key: "K", ...mod }), "Mod+K")).toBe(true);
    expect(matchesShortcut(key({ key: "k", ...other }), "Mod+K")).toBe(false);
    expect(matchesShortcut(key({ key: "k" }), "Mod+K")).toBe(false);
    expect(matchesShortcut(key({ key: "k", altKey: true, ...mod }), "Mod+K")).toBe(false);
  });

  it("0015 PRM-11: matches ? on the typed character, with or without Shift", () => {
    expect(matchesShortcut(key({ key: "?", shiftKey: true }), "?")).toBe(true);
    expect(matchesShortcut(key({ key: "?" }), "?")).toBe(true);
    expect(matchesShortcut(key({ key: "/", shiftKey: true }), "?")).toBe(false);
    expect(matchesShortcut(key({ key: "?", ctrlKey: true }), "?")).toBe(false);
  });

  it("0015 PRM-11: Kbd shows the platform's keys", async () => {
    await activateLocale("en");
    const screen = await render(
      <I18nProvider i18n={i18n}>
        <p>
          Open with <Kbd keys="Mod+K" />
        </p>
      </I18nProvider>,
    );
    const keys = [...screen.container.querySelectorAll("kbd kbd")].map((kbd) => kbd.textContent);
    expect(keys).toEqual([apple ? "⌘" : "Ctrl", "K"]);
    await expectNoAxeViolations();
  });

  it("0015 PRM-11: Mod+K matches the K key on a layout that types no Latin letter", () => {
    const mod = apple ? { metaKey: true } : { ctrlKey: true };
    // Russian: the key where K sits types л.
    expect(matchesShortcut(key({ key: "л", code: "KeyK", ...mod }), "Mod+K")).toBe(true);
    expect(matchesShortcut(key({ key: "л", code: "KeyL", ...mod }), "Mod+K")).toBe(false);
    // A Latin layout keeps the letter: on Dvorak the key at K's place types t, which is not Mod+K.
    expect(matchesShortcut(key({ key: "t", code: "KeyK", ...mod }), "Mod+K")).toBe(false);
    expect(matchesShortcut(key({ key: "k", code: "KeyV", ...mod }), "Mod+K")).toBe(true);
  });

  it("0001 UX-26: a held key fires the shortcut once, not on every repeat", async () => {
    triggered = 0;
    await activateLocale("en");
    const screen = await render(
      <I18nProvider i18n={i18n}>
        <Bound />
      </I18nProvider>,
    );
    await expect.element(screen.getByRole("button", { name: "Shortcuts" })).toBeVisible();

    document.dispatchEvent(key({ key: "?", shiftKey: true, bubbles: true }));
    document.dispatchEvent(key({ key: "?", shiftKey: true, repeat: true, bubbles: true }));
    document.dispatchEvent(key({ key: "?", shiftKey: true, repeat: true, bubbles: true }));
    expect(triggered).toBe(1);
  });

  it("0015 AC-18: Kbd keys keep their left to right order in a right to left page", async () => {
    await activateLocale("en");
    const screen = await render(
      <I18nProvider i18n={i18n}>
        <p dir="rtl" lang="ar">
          افتح <Kbd keys="Mod+K" />
        </p>
      </I18nProvider>,
    );
    const outer = screen.container.querySelector("p > kbd");
    expect(outer?.getAttribute("dir")).toBe("ltr");
    expect(outer && getComputedStyle(outer).unicodeBidi).toBe("isolate");
    const [mod, letter] = [...screen.container.querySelectorAll("kbd kbd")];
    expect(letter?.textContent).toBe("K");
    // The modifier comes first on screen too: left of the letter.
    expect(
      mod && letter && mod.getBoundingClientRect().left < letter.getBoundingClientRect().left,
    ).toBe(true);
    await expectNoAxeViolations();
  });
});
