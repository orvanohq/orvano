import { afterEach, describe, expect, it, vi } from "vitest";
import { z } from "@/shared/zod";
import { defineStored, resetStorageForTests, storageEventForTests } from "./stored";

// Spec 0003 STO-2, STO-3, STO-5, QA-4: corrupt and blocked storage never throw.

const item = defineStored({
  key: (ids: { userId: string }) => `orvano:v1:${ids.userId}:test`,
  schema: z.object({ count: z.number() }),
  fallback: { count: 0 },
});
const ids = { userId: "usr_1" };
const key = "orvano:v1:usr_1:test";

describe("defineStored", () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
    resetStorageForTests();
  });

  it("writes and reads a value", () => {
    item.write(ids, { count: 3 });

    expect(localStorage.getItem(key)).toBe('{"count":3}');
    expect(item.read(ids)).toEqual({ count: 3 });
  });

  it("returns the fallback for a missing item", () => {
    expect(item.read(ids)).toEqual({ count: 0 });
  });

  it("returns the fallback and removes an entry that isn't JSON", () => {
    localStorage.setItem(key, "{not json");

    expect(item.read(ids)).toEqual({ count: 0 });
    expect(localStorage.getItem(key)).toBeNull();
  });

  it("returns the fallback and removes an entry that fails the schema", () => {
    localStorage.setItem(key, '{"count":"three"}');

    expect(item.read(ids)).toEqual({ count: 0 });
    expect(localStorage.getItem(key)).toBeNull();
  });

  it("keeps items in memory for the session when storage is blocked (STO-3)", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("Blocked", "SecurityError");
    });

    item.write(ids, { count: 5 });

    expect(item.read(ids)).toEqual({ count: 5 });
  });

  it("calls back when another tab changes the item (STO-5)", () => {
    const listener = vi.fn<(value: { count: number }) => void>();
    const unsubscribe = item.subscribe(ids, listener);

    window.dispatchEvent(storageEventForTests(key, '{"count":7}'));
    window.dispatchEvent(storageEventForTests("other", "{}"));
    unsubscribe();
    window.dispatchEvent(storageEventForTests(key, '{"count":8}'));

    expect(listener).toHaveBeenCalledExactlyOnceWith({ count: 7 });
  });

  it("ignores a sessionStorage change with the same key (STO-5)", () => {
    const listener = vi.fn<(value: { count: number }) => void>();
    const unsubscribe = item.subscribe(ids, listener);

    window.dispatchEvent(
      new StorageEvent("storage", {
        key,
        newValue: '{"count":7}',
        storageArea: sessionStorage,
      }),
    );
    unsubscribe();

    expect(listener).not.toHaveBeenCalled();
  });

  it("goes back to the fallback when another tab clears storage (STO-5)", () => {
    const listener = vi.fn<(value: { count: number }) => void>();
    const unsubscribe = item.subscribe(ids, listener);

    // `localStorage.clear()` in another tab sends one event with a null key.
    window.dispatchEvent(storageEventForTests(null, null));
    unsubscribe();

    expect(listener).toHaveBeenCalledExactlyOnceWith({ count: 0 });
  });
});
