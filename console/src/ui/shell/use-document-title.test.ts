import { describe, expect, it } from "vitest";
import { documentTitle } from "./use-document-title";

// Spec 0001 UX-4: Resource · Service · Project · Orvano, the org left out.

describe("documentTitle", () => {
  const project = { id: "storefront", name: "Storefront" };

  it("reverses the crumbs, leaves out the org, and ends with the brand", () => {
    expect(
      documentTitle(
        [
          { kind: "org" },
          { kind: "project" },
          { kind: "page", label: "Databases" },
          { kind: "page", label: "users" },
        ],
        { project },
      ),
    ).toBe("users · Databases · Storefront · Orvano");
  });

  it("works without a project (account scope)", () => {
    expect(
      documentTitle(
        [
          { kind: "page", label: "Account" },
          { kind: "page", label: "Security" },
        ],
        {},
      ),
    ).toBe("Security · Account · Orvano");
  });
});
