import { afterEach, describe, expect, it, vi } from "vitest";
import {
  accountLocale,
  getDirection,
  reactAriaLocales,
  resolveLocale,
  sourceLocale,
} from "./locales";

// Spec 0003 I18N-6: navigator languages matched to the shipped catalogs, else en.
describe("resolveLocale", () => {
  it("picks en when the browser asks for a regional English", () => {
    expect(resolveLocale(["en-GB", "fr-FR"])).toBe("en");
  });

  it("matches the language whatever its case", () => {
    expect(resolveLocale(["EN-us"])).toBe("en");
  });

  it("falls back to the source locale when nothing requested is shipped", () => {
    expect(resolveLocale(["de-DE", "ja"])).toBe(sourceLocale);
  });

  it("falls back to the source locale when the browser sends no languages", () => {
    expect(resolveLocale([])).toBe(sourceLocale);
  });

  it("never resolves to the pseudo locale from browser languages", () => {
    expect(resolveLocale(["pseudo-LOCALE"])).toBe(sourceLocale);
  });

  it("skips malformed tags and keeps looking", () => {
    expect(resolveLocale(["", "-", "en"])).toBe("en");
  });
});

// Spec 0017 LOC-3: a stored language with no catalog falls through.
describe("accountLocale", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("0017 LOC-3: a stored `ar` ends at the source locale, since no catalog ships for it", () => {
    vi.spyOn(navigator, "languages", "get").mockReturnValue(["ar-EG", "fr"]);
    expect(accountLocale("ar")).toBe(sourceLocale);
  });

  it("follows the browser when the account has no language", () => {
    vi.spyOn(navigator, "languages", "get").mockReturnValue(["en-GB"]);
    expect(accountLocale(null)).toBe("en");
  });
});

describe("getDirection", () => {
  it.each(["ar", "ar-EG", "he", "fa-IR", "ur"])("is rtl for %s", (locale) => {
    expect(getDirection(locale)).toBe("rtl");
  });

  it.each(["en", "en-US", "fr", "pseudo-LOCALE", ""])("is ltr for %s", (locale) => {
    expect(getDirection(locale)).toBe("ltr");
  });
});

describe("reactAriaLocales", () => {
  it("maps each shipped catalog to the region tag React Aria keys its strings by", () => {
    expect(reactAriaLocales).toEqual(["en-US"]);
  });
});
