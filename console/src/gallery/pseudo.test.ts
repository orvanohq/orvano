import { afterEach, describe, expect, it } from "vitest";
import { msg } from "@lingui/core/macro";
import { activateLocale, i18n } from "@/shared/i18n";
import { pseudoize } from "./pseudo";

// Spec 0015 GAL-2, spec 0003 I18N-7: the pseudo locale grows text by about 35% with its placeholders kept, and the
// gallery's helper gives scene strings the catalog's exact transform.

afterEach(async () => {
  await activateLocale("en");
});

describe("the pseudo catalog", () => {
  it("0015 GAL-2: makes a shell string at least 30% longer than English", async () => {
    await activateLocale("pseudo-LOCALE");

    for (const english of ["Skip to content", "Search projects", "Help", "Overview"]) {
      const pseudo = i18n._({ id: idOf(english), message: english });
      expect(pseudo).not.toBe(english);
      expect(pseudo.length).toBeGreaterThanOrEqual(english.length * 1.3);
    }
  });

  it("0015 GAL-2: keeps placeholders, which take their values", async () => {
    await activateLocale("pseudo-LOCALE");

    const named = i18n._(idOf("Switch organization: {name}"), { name: "Acme" });

    expect(named).toContain("Acme");
    expect(named.length).toBeGreaterThanOrEqual("Switch organization: Acme".length * 1.3);
  });
});

describe("pseudoize", () => {
  it("0015 GAL-2: matches the catalog's transform, literal for literal", async () => {
    await activateLocale("pseudo-LOCALE");

    for (const english of ["Skip to content", "Search projects", "Help", "Overview"]) {
      expect(pseudoize(english)).toBe(i18n._({ id: idOf(english), message: english }));
    }
    expect(pseudoize("Switch organization: {name}").replace("{name}", "Acme")).toBe(
      i18n._(idOf("Switch organization: {name}"), { name: "Acme" }),
    );
  });

  it("0015 GAL-2: grows text by at least 30% and keeps placeholders", () => {
    const pseudo = pseudoize("Rows in {table}, sorted by {column}");

    expect(pseudo).toContain("{table}");
    expect(pseudo).toContain("{column}");
    const filled = pseudo.replace("{table}", "users").replace("{column}", "name");
    expect(filled.length).toBeGreaterThanOrEqual("Rows in users, sorted by name".length * 1.3);
    expect(pseudo.replaceAll(/\{\w+\}/g, "")).not.toMatch(/[a-z]/i); // every letter outside them is accented
  });
});

/** The catalog id the Lingui macro gives a message (the gallery's own strings never enter the catalog). */
function idOf(message: string): string {
  const ids: Record<string, string> = {
    "Skip to content": msg`Skip to content`.id,
    "Search projects": msg`Search projects`.id,
    Help: msg`Help`.id,
    Overview: msg`Overview`.id,
    "Switch organization: {name}": msg({ message: "Switch organization: {name}" }).id,
  };
  const id = ids[message];
  if (!id) throw new Error(`No id for ${message}`);
  return id;
}
