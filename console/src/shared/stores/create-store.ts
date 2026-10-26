import { useSyncExternalStore } from "react";

// Spec 0003 STA-2: the small store helper for shell state, read with selectors.

export interface Store<State> {
  get: () => State;
  set: (next: State | ((state: State) => State)) => void;
  subscribe: (listener: () => void) => () => void;
}

export function createStore<State>(initial: State): Store<State> {
  let state = initial;
  const listeners = new Set<() => void>();
  return {
    get: () => state,
    set: (next) => {
      const value = typeof next === "function" ? (next as (state: State) => State)(state) : next;
      if (Object.is(value, state)) return;
      state = value;
      for (const listener of listeners) listener();
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

/** Reads a slice of a store; the component re-renders only when that slice changes. */
export function useStore<State, Slice>(
  store: Store<State>,
  select: (state: State) => Slice,
): Slice {
  return useSyncExternalStore(store.subscribe, () => select(store.get()));
}
