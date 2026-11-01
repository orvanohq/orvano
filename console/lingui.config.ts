import { defineConfig } from "@lingui/cli";

// Spec 0003 I18N-3 and I18N-7: one source catalog in English, plus a pseudo locale for development. The gallery's
// strings stay out of it (spec 0015 GAL-3).
export default defineConfig({
  sourceLocale: "en",
  locales: ["en", "pseudo-LOCALE"],
  // I18N-7, GAL-2: each message grows by about 35% (brackets plus padding on both sides) with its placeholders kept,
  // so text that breaks at 30% longer (UX-34) shows. src/gallery/pseudo.ts mirrors these options for scene strings;
  // its test fails if the two drift apart.
  pseudoLocale: {
    locale: "pseudo-LOCALE",
    prepend: "[",
    append: "]",
    extend: 0.35,
    extendCharacter: "~",
  },
  fallbackLocales: { default: "en" },
  catalogs: [
    {
      path: "<rootDir>/src/locales/{locale}/messages",
      include: ["src"],
      exclude: ["src/gallery/**"],
    },
  ],
});
