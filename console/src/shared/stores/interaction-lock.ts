import { useEffect } from "react";
import { createStore } from "./create-store";

// Spec 0003 QRY-4: the interaction lock. While a form has unsaved changes (or, later, a grid cell is being edited),
// refocusing the window or reconnecting doesn't refetch, so the data under the user's edit doesn't move.

const holders = createStore(0);

/** Holds the lock while `active` is true. Holders are counted and released on unmount, so none can leave it stuck. */
export function useInteractionLock(active: boolean): void {
  useEffect(() => {
    if (!active) return;
    holders.set((count) => count + 1);
    return () => {
      holders.set((count) => count - 1);
    };
  }, [active]);
}

export function isInteractionLocked(): boolean {
  return holders.get() > 0;
}
