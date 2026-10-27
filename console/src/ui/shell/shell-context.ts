import { createContext, useContext } from "react";

// What AppShell shares with the resource pane it holds: the ids its storage item is keyed by, whether the layout
// is wide, and the narrow drawer's open state (opened by AppShell's "Show list" button).
export interface ShellContextValue {
  userId: string;
  projectId: string;
  isWide: boolean;
  listDrawerOpen: boolean;
  setListDrawerOpen: (open: boolean) => void;
}

export const ShellContext = createContext<ShellContextValue | null>(null);

export function useShellContext(): ShellContextValue {
  const value = useContext(ShellContext);
  if (!value) throw new Error("ResourceLayout renders only inside AppShell's resourceList");
  return value;
}
