import type { z } from "@/shared/zod";

// Spec 0003 STO-1 to STO-5: the one module that touches browser storage. Reads and writes never throw: a parse or
// schema failure returns the fallback and removes the bad entry (STO-2), and blocked storage falls back to memory
// for the session (STO-3).

const memory = new Map<string, string>();
let available: boolean | undefined;

/** STO-3: probes localStorage once (write and remove a test key); blocked or throwing means memory only. */
function storageAvailable(): boolean {
  if (available === undefined) {
    try {
      const probe = "orvano:probe";
      localStorage.setItem(probe, probe);
      localStorage.removeItem(probe);
      available = true;
    } catch {
      available = false;
    }
  }
  return available;
}

function getRaw(key: string): string | null {
  if (!storageAvailable()) return memory.get(key) ?? null;
  try {
    return localStorage.getItem(key);
  } catch {
    return memory.get(key) ?? null;
  }
}

function setRaw(key: string, value: string): void {
  if (storageAvailable()) {
    try {
      localStorage.setItem(key, value);
      return;
    } catch {
      // Quota or a policy change since the probe: keep it for the session instead.
    }
  }
  memory.set(key, value);
}

function removeRaw(key: string): void {
  memory.delete(key);
  if (!storageAvailable()) return;
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing to clean up when storage refuses.
  }
}

export interface StoredItem<Ids, Value> {
  read: (ids: Ids) => Value;
  write: (ids: Ids, value: Value) => void;
  remove: (ids: Ids) => void;
  /** STO-5: calls back when another tab changes this item. Returns the unsubscribe function. */
  subscribe: (ids: Ids, listener: (value: Value) => void) => () => void;
}

/** STO-2: declares a stored item. Every item lives in `items.ts`, never anywhere else. */
export function defineStored<Ids, Value>(options: {
  key: (ids: Ids) => string;
  schema: z.ZodMiniType<Value>;
  fallback: NoInfer<Value>;
}): StoredItem<Ids, Value> {
  const parse = (raw: string | null, key: string): Value => {
    if (raw === null) return options.fallback;
    try {
      const result = options.schema.safeParse(JSON.parse(raw));
      if (result.success) return result.data;
    } catch {
      // Not JSON: treated like a schema failure below.
    }
    removeRaw(key);
    return options.fallback;
  };

  return {
    read: (ids) => {
      const key = options.key(ids);
      return parse(getRaw(key), key);
    },
    write: (ids, value) => {
      setRaw(options.key(ids), JSON.stringify(value));
    },
    remove: (ids) => {
      removeRaw(options.key(ids));
    },
    subscribe: (ids, listener) => {
      const key = options.key(ids);
      const onStorage = (event: StorageEvent) => {
        // Only localStorage holds these items: a sessionStorage event with the same key isn't about them.
        if (!storageAvailable() || event.storageArea !== localStorage) return;
        // `clear()` in another tab sends a null key: every item is gone, so this one is back to its fallback.
        if (event.key === null) listener(options.fallback);
        else if (event.key === key) listener(parse(event.newValue, key));
      };
      window.addEventListener("storage", onStorage);
      return () => {
        window.removeEventListener("storage", onStorage);
      };
    },
  };
}

/** Test only: the `storage` event another tab's change to localStorage fires in this one (`key` null for `clear()`). */
export function storageEventForTests(key: string | null, newValue: string | null): StorageEvent {
  return new StorageEvent("storage", { key, newValue, storageArea: localStorage });
}

/** Test only: forgets the probe result and the in memory items. */
export function resetStorageForTests(): void {
  available = undefined;
  memory.clear();
}
