import { defineConfig, globalIgnores } from "eslint/config";
import js from "@eslint/js";
import tseslint from "typescript-eslint";
import astro from "eslint-plugin-astro";
import globals from "globals";

// Recommended JavaScript, TypeScript and Astro rules for the docs site.
// `any` is an error, not a warning (AGENTS.md: strict, no `any`).
export default defineConfig([
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...astro.configs.recommended,
  {
    languageOptions: { globals: { ...globals.node } },
    rules: {
      "@typescript-eslint/no-explicit-any": "error",
    },
  },
  globalIgnores(["dist/**", ".astro/**"]),
]);
