import { layout, type Density, type Layout } from "@/shared/storage/items";
import { createStore, useStore, type Store } from "./create-store";

// Spec 0015 SHL-11: the one store for `orvano:v1:{userId}:layout` (sidebar collapsed, density, and UX-37's last SDK).
// Updates go field by field over the latest stored value, so its three writers never overwrite each other.

export type { Density, Layout };

// One signed in user at a time: switching user drops the previous user's store and its storage listener.
let current: { userId: string; store: Store<Layout>; unsubscribe: () => void } | undefined;

function storeFor(userId: string): Store<Layout> {
  if (current?.userId !== userId) {
    current?.unsubscribe();
    const store = createStore<Layout>(layout.read({ userId }));
    // STO-5: another tab's change shows here too.
    const unsubscribe = layout.subscribe({ userId }, (value) => {
      store.set(value);
      applyDensity(value.density);
    });
    current = { userId, store, unsubscribe };
  }
  return current.store;
}

/** Sets `data-density` on <html>, which switches `--row-height` (UX-32). */
function applyDensity(density: Density): void {
  document.documentElement.dataset.density = density;
}

/** Changes some fields, keeping the others as the latest stored value has them. */
export function updateLayout(userId: string, patch: Partial<Layout>): void {
  const next = { ...layout.read({ userId }), ...patch };
  layout.write({ userId }, next);
  storeFor(userId).set(next);
}

/** Saves the density and applies it at once. */
export function setDensity(userId: string, density: Density): void {
  updateLayout(userId, { density });
  applyDensity(density);
}

/**
 * The boot step (spec 0003 RTE-10): applies the saved density before the first render. Feature 13 calls it once
 * the session loads; until then the gallery does.
 */
export function applyStoredDensity(userId: string): void {
  applyDensity(storeFor(userId).get().density);
}

export function useLayout<Slice>(userId: string, select: (state: Layout) => Slice): Slice {
  return useStore(storeFor(userId), select);
}

/** Test only: forgets the per user stores. */
export function resetLayoutStoresForTests(): void {
  current?.unsubscribe();
  current = undefined;
}
