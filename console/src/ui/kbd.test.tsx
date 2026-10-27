import { describe, expect, it } from "vitest";
import { render } from "vitest-browser-react";
import { I18nProvider } from "@lingui/react";
import { activateLocale, i18n } from "@/shared/i18n";
import { expectNoAxeViolations } from "@/test/axe";
import { ariaKeyshortcuts, Kbd } from "./kbd";
import { isApplePlatform } from "./platform";
import { matchesShortcut } from "./use-shortcut";

const apple = isApplePlatform();

function key(init: KeyboardEventInit) {
  return new KeyboardEvent("keydown", init);
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
});
