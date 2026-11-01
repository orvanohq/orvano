// Spec 0003 I18N-6: the one locale module.
export const sourceLocale = "en";

export const shippedLocales = ["en"] as const;
export type Locale = (typeof shippedLocales)[number] | "pseudo-LOCALE";

// React Aria keys its own strings by region tag, so each shipped language maps to the tag whose strings it reads.
const reactAriaRegions: Readonly<Record<string, string>> = { en: "en-US" };

/** The Language field shows only when there's a language to choose (spec 0017 ACC-3). */
export const hasLanguageChoice = (shippedLocales as readonly Locale[]).length > 1;

/** The React Aria locales the build keeps, one per shipped catalog (vite.config.ts, I18N-6). */
export const reactAriaLocales = shippedLocales.map((locale) => reactAriaRegions[locale] ?? locale);

const rightToLeft = new Set(["ar", "fa", "he", "ur"]);

export function resolveLocale(requested: readonly string[]): Locale {
  for (const tag of requested) {
    const language = tag.toLowerCase().split("-")[0];
    const match = shippedLocales.find((locale) => locale === language);
    if (match) return match;
  }
  return sourceLocale;
}

/** I18N-6: the account's language, then the browser's, then `en`. */
export function accountLocale(account: string | null | undefined): Locale {
  return resolveLocale(account ? [account, ...navigator.languages] : navigator.languages);
}

export function getDirection(locale: string): "ltr" | "rtl" {
  const language = locale.toLowerCase().split("-")[0] ?? "";
  return rightToLeft.has(language) ? "rtl" : "ltr";
}
