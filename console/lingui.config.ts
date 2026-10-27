import { defineConfig } from "@lingui/cli";

// Spec 0003 I18N-3 and I18N-7: one source catalog in English, plus a pseudo locale for development. The gallery's
// strings stay out of it (spec 0015 GAL-3), and so do the size probe's (STY-9).
export default defineConfig({
  sourceLocale: "en",
  locales: ["en", "pseudo-LOCALE"],
  pseudoLocale: "pseudo-LOCALE",
  fallbackLocales: { default: "en" },
  catalogs: [
    {
      path: "<rootDir>/src/locales/{locale}/messages",
      include: ["src"],
      exclude: ["src/gallery/**", "src/size-probe/**"],
    },
  ],
});
