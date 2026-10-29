import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { themePreference } from "@/shared/storage/items";
import { resetStorageForTests, storageEventForTests } from "@/shared/storage/stored";
import { setThemePreference, startThemeSync, themeStore } from "./theme";

// Spec 0015 THM-1, THM-4. The OS theme change and the boot script are covered by e2e/theme-boot.spec.ts.

const systemTheme = () =>
  window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";

describe("theme store", () => {
  let stop: (() => void) | undefined;
  const html = document.documentElement;

  beforeEach(() => {
    themePreference.remove(undefined);
    resetStorageForTests();
  });

  afterEach(() => {
    stop?.();
    themePreference.remove(undefined);
    delete html.dataset.theme;
    html.style.colorScheme = "";
  });

  it("0015 AC-4: defaults to System, resolved from the OS", () => {
    stop = startThemeSync();

    expect(themeStore.get()).toEqual({ preference: "system", resolved: systemTheme() });
    expect(html.dataset.theme).toBe(systemTheme());
    expect(html.style.colorScheme).toBe(systemTheme());
  });

  it("0015 AC-4: treats an invalid stored value as System", () => {
    themePreference.write(undefined, "sepia" as never);

    stop = startThemeSync();

    expect(themeStore.get().preference).toBe("system");
  });

  it("0015 AC-4: changing the preference saves it and updates <html> at once", () => {
    stop = startThemeSync();

    setThemePreference("dark");

    expect(themePreference.read(undefined)).toBe("dark");
    expect(themeStore.get()).toEqual({ preference: "dark", resolved: "dark" });
    expect(html.dataset.theme).toBe("dark");
    expect(html.style.colorScheme).toBe("dark");
  });

  it("0015 AC-4: follows a change made in another tab (STO-5)", () => {
    stop = startThemeSync();

    window.dispatchEvent(storageEventForTests("orvano:v1:theme", '"light"'));

    expect(themeStore.get().preference).toBe("light");
    expect(html.dataset.theme).toBe("light");
  });
});
