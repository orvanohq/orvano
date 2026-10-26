import { themePreference, type ThemePreference } from "@/shared/storage/items";
import { createStore, useStore } from "./create-store";

// Spec 0015 THM-4: the theme preference and the theme it resolves to. public/theme-boot.js applies the same
// result before the first paint; this store keeps <html> current after that.

export type { ThemePreference };
export type ResolvedTheme = "light" | "dark";

interface ThemeState {
  preference: ThemePreference;
  resolved: ResolvedTheme;
}

function resolve(preference: ThemePreference): ResolvedTheme {
  if (preference !== "system") return preference;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export const themeStore = createStore<ThemeState>({ preference: "system", resolved: "light" });

/** Writes the resolved theme to <html>, like theme-boot.js. Never animated (THM-5). */
function apply(state: ThemeState): void {
  const root = document.documentElement;
  root.dataset.theme = state.resolved;
  root.style.colorScheme = state.resolved;
}

function update(preference: ThemePreference): void {
  const next = { preference, resolved: resolve(preference) };
  themeStore.set(next);
  apply(next);
}

/** Changes the preference: saves it (STO-2) and updates <html> at once. */
export function setThemePreference(preference: ThemePreference): void {
  themePreference.write(undefined, preference);
  update(preference);
}

/**
 * Reads the saved preference, applies it, and follows the OS theme (under System) and other tabs (STO-5) until
 * the returned function is called. main.tsx and the gallery call it once at boot.
 */
export function startThemeSync(): () => void {
  update(themePreference.read(undefined));
  const media = window.matchMedia("(prefers-color-scheme: dark)");
  const onMediaChange = () => {
    if (themeStore.get().preference === "system") update("system");
  };
  media.addEventListener("change", onMediaChange);
  const unsubscribe = themePreference.subscribe(undefined, update);
  return () => {
    media.removeEventListener("change", onMediaChange);
    unsubscribe();
  };
}

export function useThemePreference(): ThemePreference {
  return useStore(themeStore, (state) => state.preference);
}

export function useResolvedTheme(): ResolvedTheme {
  return useStore(themeStore, (state) => state.resolved);
}
