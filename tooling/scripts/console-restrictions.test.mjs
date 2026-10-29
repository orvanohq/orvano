// Spec 0008 LNT-3: eslint.config.ts spreads regionRules() as is, so these pin which restriction applies where.
import assert from "node:assert/strict";
import { describe, test } from "node:test";
import { ESLint } from "eslint";
import {
  regionRules,
  regions,
  restrictionMessages,
  restrictions,
} from "../config/console-restrictions.ts";

/** @param {string} file */
const rulesFor = (file) => {
  const region = regionRules().find((entry) => entry.files.includes(`console/src/${file}`));
  assert.ok(region, `no region for ${file}`);
  return region.rules;
};

/**
 * Every rule ID a region's rules can report, from each message's `ID:` prefix.
 * @param {string} file
 */
const idsFor = (file) => {
  const rules = rulesFor(file);
  /** @type {string[]} */
  const messages = [];
  const imports = rules["no-restricted-imports"];
  if (imports !== "off") {
    const options = /** @type {{ patterns: { message: string }[] }} */ (imports[1]);
    messages.push(...options.patterns.map((pattern) => pattern.message));
  }
  for (const rule of /** @type {const} */ (["no-restricted-globals", "no-restricted-syntax"])) {
    const entry = rules[rule];
    if (entry === "off") continue;
    const options = /** @type {{ message: string }[]} */ (entry.slice(1));
    messages.push(...options.map((option) => option.message));
  }
  return new Set(messages.map((message) => message.split(":")[0]));
};

/**
 * The messages of a region's import restrictions.
 * @param {string} file
 */
const importMessagesFor = (file) => {
  const imports = rulesFor(file)["no-restricted-imports"];
  if (imports === "off") return [];
  const options = /** @type {{ patterns: { message: string }[] }} */ (imports[1]);
  return options.patterns.map((pattern) => pattern.message);
};

/**
 * The global names a region restricts.
 * @param {string} file
 */
const globalsFor = (file) => {
  const entry = rulesFor(file)["no-restricted-globals"];
  if (entry === "off") return [];
  return /** @type {{ name: string }[]} */ (entry.slice(1)).map((global) => global.name);
};

/**
 * The anchored regex each banned package list compiles to, by its message's rule ID.
 * @param {string} message
 */
const importRegex = (message) => {
  const restriction = restrictions.find((entry) => entry.imports?.message.startsWith(message));
  const imports = restriction?.imports;
  assert.ok(imports && "regex" in imports, `no regex restriction for ${message}`);
  return new RegExp(imports.regex);
};

describe("regionRules", () => {
  test("returns one rule set per region, in order, with the region's files", () => {
    const rules = regionRules();
    assert.equal(rules.length, regions.length);
    rules.forEach((entry, index) => assert.deepEqual(entry.files, regions[index]?.files));
  });

  test("every rule set is an error or off for all three restriction rules", () => {
    for (const { rules } of regionRules()) {
      for (const entry of Object.values(rules)) {
        assert.ok(entry === "off" || entry[0] === "error");
      }
    }
  });

  test("shared/config.ts drops only WEB-8 and keeps the rest", () => {
    const config = idsFor("shared/config.ts");
    const shared = idsFor("shared/**/*.{ts,tsx}");
    assert.ok(!config.has("WEB-8"));
    assert.ok(shared.has("WEB-8"));
    assert.deepEqual([...config].toSorted(), [...shared].filter((id) => id !== "WEB-8").toSorted());
    for (const id of ["QRY-1", "STO-1", "SEC-2", "DIR-2", "DIR-3"]) assert.ok(config.has(id), id);
  });

  test("spec 0015 STY-8: only shared/zod.ts imports zod, and every other region forbids it", () => {
    assert.ok(!idsFor("shared/zod.ts").has("STY-8"));
    for (const region of regions) {
      const file = region.files[0]?.replace("console/src/", "") ?? "";
      if (file !== "shared/zod.ts") assert.ok(idsFor(file).has("STY-8"), file);
    }
    const zod = importRegex("STY-8");
    for (const name of ["zod", "zod/mini", "zod/v4", "zod/v4/core"])
      assert.ok(zod.test(name), name);
    assert.ok(!zod.test("@/shared/zod"));
    assert.ok(!zod.test("zodiac"));
  });

  test("shared/api may call fetch, but keeps every other rule", () => {
    assert.ok(!globalsFor("shared/api/**/*.{ts,tsx}").includes("fetch"));
    assert.ok(globalsFor("shared/api/**/*.{ts,tsx}").includes("localStorage"));
    assert.ok(globalsFor("shared/**/*.{ts,tsx}").includes("fetch"));
    // QRY-1's Replaces list (axios and friends) still applies there.
    assert.ok(idsFor("shared/api/**/*.{ts,tsx}").has("QRY-1"));
    assert.ok(idsFor("shared/api/**/*.{ts,tsx}").has("WEB-8"));
  });

  test("shared/storage may touch browser storage", () => {
    assert.ok(!globalsFor("shared/storage/**/*.{ts,tsx}").includes("localStorage"));
    assert.ok(globalsFor("shared/storage/**/*.{ts,tsx}").includes("fetch"));
  });

  test("queries.ts drops only QRY-2, view hooks and shared/url drop only URL-3", () => {
    assert.ok(!idsFor("features/*/api/queries.ts").has("QRY-2"));
    assert.ok(idsFor("features/*/api/queries.ts").has("URL-3"));
    assert.ok(!idsFor("features/**/use-*-view.{ts,tsx}").has("URL-3"));
    assert.ok(idsFor("features/**/use-*-view.{ts,tsx}").has("QRY-2"));
    assert.ok(!idsFor("shared/url/**/*.{ts,tsx}").has("URL-3"));
    assert.ok(idsFor("features/**/*.{ts,tsx}").has("QRY-2"));
    assert.ok(idsFor("features/**/*.{ts,tsx}").has("URL-3"));
  });

  test("the import boundary (DIR-2) applies only to features, shared, and ui", () => {
    assert.ok(!idsFor("**/*.{ts,tsx}").has("DIR-2"));
    assert.ok(!idsFor("routes/**/*.{ts,tsx}").has("DIR-2"));
    assert.ok(idsFor("features/**/*.{ts,tsx}").has("DIR-2"));
    assert.ok(idsFor("ui/**/*.{ts,tsx}").has("DIR-2"));
  });

  test("ui may import React Aria, while other areas may not", () => {
    const reactAria = "DIR-3: compose the primitives in src/ui instead";
    assert.ok(
      !importMessagesFor("ui/**/*.{ts,tsx}").some((message) => message.startsWith(reactAria)),
    );
    // ui still can't pull in another component library (also DIR-3).
    assert.ok(
      importMessagesFor("ui/**/*.{ts,tsx}").some((message) => message.includes("never another")),
    );
    assert.ok(
      importMessagesFor("routes/**/*.{ts,tsx}").some((message) => message.startsWith(reactAria)),
    );
  });

  test("spec 0015: STY-5 and GAL-3 apply outside src/ui and the gallery, RTL-2 everywhere", () => {
    for (const file of ["**/*.{ts,tsx}", "features/**/*.{ts,tsx}", "shared/**/*.{ts,tsx}"]) {
      assert.ok(idsFor(file).has("STY-5"), file);
      assert.ok(idsFor(file).has("GAL-3"), file);
    }
    assert.ok(!idsFor("ui/**/*.{ts,tsx}").has("STY-5"));
    assert.ok(idsFor("ui/**/*.{ts,tsx}").has("GAL-3"));
    assert.ok(idsFor("gallery/**/*.{ts,tsx}").has("STY-5"));
    assert.ok(!idsFor("gallery/**/*.{ts,tsx}").has("GAL-3"));
    for (const region of regions) {
      const file = region.files[0]?.replace("console/src/", "") ?? "";
      assert.ok(idsFor(file).has("RTL-2"), file);
    }
  });

  test("spec 0015: only ui may import Lucide and the grid (ICN-1, PRM-5)", () => {
    const dir3 = importRegex("DIR-3: compose the primitives in src/ui instead");
    assert.ok(dir3.test("lucide-react"));
    assert.ok(dir3.test("react-data-grid"));
    assert.ok(!dir3.test("lucide"));
    assert.ok(idsFor("gallery/**/*.{ts,tsx}").has("DIR-3"));
  });
});

describe("package regexes", () => {
  test("match the package and its subpaths, anchored", () => {
    const routing = importRegex("RTE-1");
    assert.ok(routing.test("react-router"));
    assert.ok(routing.test("react-router-dom"));
    assert.ok(routing.test("react-router/dom"));
    assert.ok(routing.test("next/link"));
    assert.ok(routing.test("@remix-run/react"));
    assert.ok(!routing.test("@tanstack/react-router"));
    assert.ok(!routing.test("nextra"));
  });

  test("scope wildcards match one segment", () => {
    const libraries = importRegex("DIR-3: compose the primitives in src/ui (React Aria inside)");
    assert.ok(libraries.test("@radix-ui/react-dialog"));
    assert.ok(libraries.test("@mui/material/Button"));
    assert.ok(!libraries.test("@radix-ui"));
    assert.ok(!libraries.test("react-aria-components"));
  });
});

describe("restrictionMessages", () => {
  test("groups the 21 rule IDs that check:lint-fixtures expects to fire", () => {
    assert.deepEqual([...restrictionMessages().keys()].toSorted(), [
      "DIR-2",
      "DIR-3",
      "FRM-1",
      "GAL-3",
      "I18N-1",
      "I18N-5",
      "QA-1",
      "QRY-1",
      "QRY-2",
      "RTE-1",
      "RTE-8",
      "RTL-2",
      "SEC-2",
      "STA-1",
      "STO-1",
      "STY-5",
      "STY-8",
      "URL-3",
      "WEB-4",
      "WEB-6",
      "WEB-8",
    ]);
  });

  test("every message starts with its own ID and appears once", () => {
    for (const [id, messages] of restrictionMessages()) {
      assert.equal(new Set(messages).size, messages.length, id);
      for (const message of messages) assert.ok(message.startsWith(`${id}: `), message);
    }
  });

  test("a message repeated across imports, globals, and syntax is listed once", () => {
    const qry1 = restrictionMessages().get("QRY-1") ?? [];
    assert.deepEqual(qry1.toSorted(), [
      "QRY-1: HTTP goes through src/shared/api, and server state lives in TanStack Query.",
      "QRY-1: call the API through the client in src/shared/api.",
    ]);
  });

  test("covers every restriction's messages", () => {
    const all = new Set([...restrictionMessages().values()].flat());
    for (const restriction of restrictions) {
      for (const entry of [
        ...(restriction.imports ? [restriction.imports] : []),
        ...(restriction.globals ?? []),
        ...(restriction.syntax ?? []),
      ]) {
        assert.ok(all.has(entry.message), entry.message);
      }
    }
  });
});

describe("syntax restrictions, linted", { timeout: 60_000 }, () => {
  // Only the restriction rules, and no type information, as in check-lint-fixtures.mjs.
  const eslint = new ESLint({
    ruleFilter: ({ ruleId }) => ruleId === "no-restricted-syntax",
    overrideConfig: [
      { languageOptions: { parserOptions: { projectService: false, project: null } } },
    ],
  });

  /** The rule IDs each line of `code` reports, linted as a feature file. @param {string} code */
  const idsByLine = async (code) => {
    const [result] = await eslint.lintText(code, {
      filePath: "console/src/features/tables/components/rows-page.tsx",
    });
    return (result?.messages ?? []).map((message) => message.message.split(":")[0]);
  };

  test("SEC-2 fails each raw HTML sink", async () => {
    for (const sink of [
      "element.innerHTML = html;",
      "element.outerHTML += html;",
      'element["innerHTML"] = html;',
      'element.insertAdjacentHTML("beforeend", html);',
      "document.write(html);",
      "document.writeln(html);",
    ]) {
      // oxlint-disable-next-line no-await-in-loop -- one sink at a time
      const ids = await idsByLine(
        `declare const element: HTMLElement;\ndeclare const html: string;\n${sink}\n`,
      );
      assert.deepEqual(ids, ["SEC-2"], sink);
    }
    assert.deepEqual(
      await idsByLine("declare const element: HTMLElement;\nelement.textContent = 'x';\n"),
      [],
    );
  });

  test("spec 0015 STY-5 fails arbitrary values behind variants and in cx templates", async () => {
    for (const classes of [
      '<span className="hover:[mask-type:luminance]" />',
      '<span className="![mask-type:luminance]" />',
      '<span className="[&>svg]:size-4" />',
      '<span className="md:w-[13px]" />',
      "<span className={cx(`${tone} w-[13px]`)} />",
      "<span className={cx(`[&>svg]:size-4 ${tone}`)} />",
    ]) {
      // oxlint-disable-next-line no-await-in-loop -- one class string at a time
      const ids = await idsByLine(
        `declare const cx: (...classes: string[]) => string;\ndeclare const tone: string;\nexport const x = ${classes};\n`,
      );
      assert.deepEqual(ids, ["STY-5"], classes);
    }
    assert.deepEqual(
      await idsByLine('export const x = <span className="w-full hover:bg-bg-muted" />;\n'),
      [],
    );
  });
});
