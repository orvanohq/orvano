import { afterEach, describe, expect, it } from "vitest";
import { activateLocale, i18n } from "./index";

// Spec 0003 RTE-10 and I18N-3: activating a locale loads its catalog and sets <html lang dir>.
describe("activateLocale", () => {
  afterEach(async () => {
    await activateLocale("en");
  });

  it("activates en and marks the page English, left to right", async () => {
    await activateLocale("en");

    expect(i18n.locale).toBe("en");
    expect(document.documentElement.lang).toBe("en");
    expect(document.documentElement.dir).toBe("ltr");
  });

  it("loads the pseudo catalog lazily and translates through it", async () => {
    await activateLocale("pseudo-LOCALE");

    expect(i18n.locale).toBe("pseudo-LOCALE");
    expect(document.documentElement.lang).toBe("pseudo-LOCALE");
    expect(i18n._("5lWFkC")).not.toBe("Sign in");
  });
});
