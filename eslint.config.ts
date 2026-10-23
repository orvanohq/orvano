import { defineConfig } from "eslint/config";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import pluginQuery from "@tanstack/eslint-plugin-query";
import pluginRouter from "@tanstack/eslint-plugin-router";
import pluginLingui from "eslint-plugin-lingui";
import oxlint from "eslint-plugin-oxlint";
import { ignorePatterns } from "./tooling/config/ignore-patterns.ts";
import { regionRules } from "./tooling/config/console-restrictions.ts";

// Spec 0008 LNT-2: ESLint runs second, only for what Oxlint can't do yet. One config, one `files` block per package.
const consoleSrc = ["console/src/**/*.{ts,tsx}"];
const consoleTests = ["console/src/**/*.test.{ts,tsx}", "console/src/test/**/*.{ts,tsx}"];
// The console's tool configs live in tsconfig.node.json, which the project service can't find by name.
const consoleToolConfigs = ["console/*.config.ts"];
const rootFiles = [
  "*.config.ts",
  "tooling/config/*.ts",
  "tooling/scripts/*.mjs",
  "api/conformance/stub/*.mjs",
];
// The SDK generator (spec 0014 GNR-1); its runtime templates import generated modules, so they are checked
// through the rendered snapshots instead.
const generatorFiles = [
  "api/generator/src/**/*.ts",
  "api/generator/test/*.ts",
  "api/generator/*.config.ts",
];

export default defineConfig(
  // Lint fixtures break the rules on purpose; check:lint-fixtures lints them on its own (LNT-3).
  { ignores: [...ignorePatterns, "tooling/lint-fixtures/**"] },
  { linterOptions: { reportUnusedDisableDirectives: "error" } },

  // typescript-eslint strict type checked (WEB-4, TYP-2) for every TypeScript and script file.
  {
    files: [...consoleSrc, ...consoleToolConfigs, ...rootFiles, ...generatorFiles],
    extends: [tseslint.configs.strictTypeChecked],
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
  },
  {
    files: consoleToolConfigs,
    languageOptions: {
      parserOptions: {
        projectService: false,
        project: "./console/tsconfig.node.json",
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },

  // The console app.
  {
    files: consoleSrc,
    extends: [
      reactHooks.configs.flat["recommended-latest"],
      pluginQuery.configs["flat/recommended"],
      pluginRouter.configs["flat/recommended"],
      pluginLingui.configs["flat/recommended"],
    ],
    rules: {
      // WEB-6: React Compiler diagnostics are errors, so a component the compiler skips fails lint.
      ...Object.fromEntries(
        Object.keys(reactHooks.configs.flat["recommended-latest"].rules).map((rule) => [
          rule,
          "error",
        ]),
      ),
      // I18N-8: literal text in JSX and user facing props goes through Lingui.
      "lingui/no-unlocalized-strings": [
        "error",
        {
          // Single tokens that don't start with a capital (locale tags, option values, paths) are code, not copy.
          ignore: ["^Orvano$", "^(?![A-Z])\\S+$"],
          ignoreNames: [
            {
              regex: {
                pattern:
                  "^(className|class|style|type|id|key|name|role|to|href|src|srcSet|rel|target|method|path|lang|dir|autoComplete|inputMode|htmlFor|form|as)$",
              },
            },
            {
              regex: {
                pattern:
                  "^(aria-(?!label|description|placeholder|roledescription|valuetext)|data-)",
              },
            },
            { regex: { pattern: "^[A-Z0-9_]+$" } },
          ],
          ignoreFunctions: [
            "Error",
            "TypeError",
            "RangeError",
            "console.*",
            "createFileRoute",
            "createRootRoute",
            "createRootRouteWithContext",
            "createLazyFileRoute",
            "document.getElementById",
            "document.querySelector",
            "*.addEventListener",
            "*.removeEventListener",
            "*.startsWith",
            "*.endsWith",
            "*.includes",
            "*.split",
            "*.join",
            "URL",
            "URLSearchParams",
            "Intl.*",
          ],
          useTsTypes: true,
          ignoreMethodsOnTypes: [
            "Map.get",
            "Map.has",
            "Set.has",
            "Headers.get",
            "URLSearchParams.get",
          ],
        },
      ],
    },
  },

  // LNT-3: restrictions, one rule set per region; an allowed home leaves out only its own restriction.
  ...regionRules(),

  // Spec 0003 Exceptions: tests may use literal strings and `!`.
  {
    files: consoleTests,
    rules: {
      "lingui/no-unlocalized-strings": "off",
      "@typescript-eslint/no-non-null-assertion": "off",
    },
  },

  // Scripts and plain JS are checked by tsc's checkJs; skip the type aware rules there.
  { files: ["tooling/scripts/*.mjs"], extends: [tseslint.configs.disableTypeChecked] },

  // LNT-2: last, so no rule runs in both linters.
  ...oxlint.buildFromOxlintConfigFile("./.oxlintrc.json"),
);
