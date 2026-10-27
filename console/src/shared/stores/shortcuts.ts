import type { MessageDescriptor } from "@lingui/core";
import { createStore, useStore } from "./create-store";

// Spec 0003 A11Y-7: the shell's shortcut registry. Only `useShortcut` in src/ui adds to it, and `ShortcutHelp` lists
// it, so the `?` list can't drift from what's bound. In memory only.

export interface RegisteredShortcut {
  id: string;
  /** `Mod+K` or a typed character like `?` (spec 0015 PRM-11). */
  keys: string;
  /** What it does ("Open the command palette"), translated when listed. */
  label: MessageDescriptor;
}

const shortcuts = createStore<readonly RegisteredShortcut[]>([]);

/** Adds a shortcut (replacing one with the same id) and returns the function that removes it. */
export function registerShortcut(shortcut: RegisteredShortcut): () => void {
  shortcuts.set((list) => [...list.filter((entry) => entry.id !== shortcut.id), shortcut]);
  return () => {
    shortcuts.set((list) => list.filter((entry) => entry !== shortcut));
  };
}

export function useShortcuts(): readonly RegisteredShortcut[] {
  return useStore(shortcuts, (list) => list);
}
