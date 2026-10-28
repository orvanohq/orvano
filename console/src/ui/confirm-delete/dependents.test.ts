import { beforeAll, describe, expect, it } from "vitest";
import { activateLocale, i18n } from "@/shared/i18n";
import { dependentText } from "./dependents";
import type { ResourceKind } from "./tiers";

// Spec 0015 AC-17, spec 0001 UX-23: the "what goes with it" lines ConfirmDelete lists, one per dependent kind.

const kinds: Record<ResourceKind | "row", [one: string, other: string]> = {
  organization: ["1 organization", "3 organizations"],
  project: ["1 project", "3 projects"],
  database: ["1 database", "3 databases"],
  bucket: ["1 bucket", "3 buckets"],
  table: ["1 table", "3 tables"],
  column: ["1 column", "3 columns"],
  index: ["1 index", "3 indexes"],
  function: ["1 function", "3 functions"],
  deployment: ["1 deployment", "3 deployments"],
  site: ["1 site", "3 sites"],
  domain: ["1 domain", "3 domains"],
  api_key: ["1 API key", "3 API keys"],
  webhook: ["1 webhook", "3 webhooks"],
  messaging_provider: ["1 messaging provider", "3 messaging providers"],
  messaging_topic: ["1 messaging topic", "3 messaging topics"],
  end_user: ["1 user", "3 users"],
  console_member: ["1 member", "3 members"],
  backup: ["1 backup", "3 backups"],
  file: ["1 file", "3 files"],
  row: ["1 row", "3 rows"],
};

describe("dependentText", () => {
  beforeAll(async () => {
    if (!i18n.locale) await activateLocale("en");
  });

  it("0015 AC-17: names every resource kind, singular and plural", () => {
    for (const [kind, [one, other]] of Object.entries(kinds)) {
      expect(dependentText(i18n, { kind, count: 1, estimated: false })).toBe(one);
      expect(dependentText(i18n, { kind, count: 3, estimated: false })).toBe(other);
    }
  });

  it("0001 UX-23: an estimate reads as about, with the count grouped", () => {
    expect(dependentText(i18n, { kind: "row", count: 1200, estimated: true })).toBe(
      "about 1,200 rows",
    );
  });

  it("0001 UX-23: zero takes the plural", () => {
    expect(dependentText(i18n, { kind: "table", count: 0, estimated: false })).toBe("0 tables");
  });

  it("a kind the console doesn't know yet still shows its count and name", () => {
    expect(dependentText(i18n, { kind: "queue", count: 4, estimated: false })).toBe(
      "4 of kind queue",
    );
    expect(dependentText(i18n, { kind: "queue", count: 4, estimated: true })).toBe(
      "about 4 of kind queue",
    );
  });

  // Known bug (found by /test, 2026-10-27): `kind in counted` (dependents.ts:48) also matches inherited names, so
  // "toString" calls Object.prototype.toString and the line reads "[object Object]". Drop `.fails` once it's fixed.
  it.fails("a kind named like an Object built in is not mistaken for a known kind", () => {
    expect(dependentText(i18n, { kind: "toString", count: 2, estimated: false })).toBe(
      "2 of kind toString",
    );
  });
});
