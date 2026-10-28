import { afterEach, describe, expect, it } from "vitest";
import { layout } from "@/shared/storage/items";
import { resetStorageForTests } from "@/shared/storage/stored";
import { applyStoredDensity, resetLayoutStoresForTests, setDensity, updateLayout } from "./layout";

// Spec 0015 SHL-11: one store for the layout item, updated field by field.

const ids = { userId: "usr_1" };

describe("layout store", () => {
  afterEach(() => {
    layout.remove(ids);
    resetStorageForTests();
    resetLayoutStoresForTests();
    delete document.documentElement.dataset.density;
  });

  it("defaults to an expanded sidebar and comfortable density", () => {
    expect(layout.read(ids)).toEqual({ sidebarCollapsed: false, density: "comfortable" });
  });

  it("updates one field without overwriting the others, including one another writer saved", () => {
    // UX-37's picker saved the last SDK.
    layout.write(ids, { sidebarCollapsed: false, density: "comfortable", lastSdk: "dart" });

    updateLayout("usr_1", { sidebarCollapsed: true });
    setDensity("usr_1", "compact");

    expect(layout.read(ids)).toEqual({
      sidebarCollapsed: true,
      density: "compact",
      lastSdk: "dart",
    });
    expect(document.documentElement.dataset.density).toBe("compact");
  });

  it("keeps the valid fields when one is corrupt", () => {
    layout.write(ids, { sidebarCollapsed: true, density: "tiny" as never });

    expect(layout.read(ids)).toEqual({ sidebarCollapsed: true, density: "comfortable" });
  });

  it("0015 AC-11: applies the saved density at boot", () => {
    layout.write(ids, { sidebarCollapsed: false, density: "compact" });

    applyStoredDensity("usr_1");

    expect(document.documentElement.dataset.density).toBe("compact");
  });

  it("0015 AC-11: a density change in another tab applies here too (STO-5)", () => {
    // The first write creates this user's store, which starts listening for other tabs.
    setDensity("usr_1", "comfortable");

    // What the other tab does: it saves the item, and this tab gets a storage event.
    const saved = { sidebarCollapsed: true, density: "compact" } as const;
    layout.write(ids, saved);
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: "orvano:v1:usr_1:layout",
        newValue: JSON.stringify(saved),
      }),
    );

    expect(document.documentElement.dataset.density).toBe("compact");
  });

  it("ignores another tab's change to a different user's layout", () => {
    setDensity("usr_1", "comfortable");

    window.dispatchEvent(
      new StorageEvent("storage", {
        key: "orvano:v1:usr_2:layout",
        newValue: JSON.stringify({ sidebarCollapsed: false, density: "compact" }),
      }),
    );

    expect(document.documentElement.dataset.density).toBe("comfortable");
  });
});
