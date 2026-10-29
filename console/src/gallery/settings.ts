import {
  layout,
  paletteRecents,
  resourceListLayout,
  themePreference,
} from "@/shared/storage/items";
import { createStore, useStore } from "@/shared/stores/create-store";

// The gallery toolbar's settings (theme and density live in the real stores, so the frame shares them through
// storage). A frame (the narrow width preview) reads
// them from its URL, so the outer gallery and the frame agree.

export type GalleryLocale = "en" | "pseudo-LOCALE";
export type Direction = "ltr" | "rtl";
export type Width = "full" | "800" | "360" | "320";
/** GAL-2: how the fixtures answer, so loading, error, and empty states can be seen without a server. */
export type Scenario = "normal" | "slow" | "fail" | "empty";

export interface GallerySettings {
  locale: GalleryLocale;
  direction: Direction;
  width: Width;
  scenario: Scenario;
}

const query = new URLSearchParams(window.location.search);
const scenarios: readonly Scenario[] = ["normal", "slow", "fail", "empty"];
const queryScenario = query.get("scenario");

/** True inside the narrow width frame, which renders the scene without the toolbar. */
export const inFrame = query.has("frame");

export const settingsStore = createStore<GallerySettings>({
  locale: query.get("locale") === "pseudo-LOCALE" ? "pseudo-LOCALE" : "en",
  direction: query.get("direction") === "rtl" ? "rtl" : "ltr",
  width: "full",
  scenario: scenarios.find((scenario) => scenario === queryScenario) ?? "normal",
});

export function useSettings(): GallerySettings {
  return useStore(settingsStore, (state) => state);
}

/** The frame's URL: the same gallery with the frame flag, the settings, and the current scene. */
export function frameUrl(settings: GallerySettings, hash: string): string {
  const params = new URLSearchParams({
    frame: "",
    locale: settings.locale,
    direction: settings.direction,
    scenario: settings.scenario,
  });
  return `${window.location.pathname}?${params.toString()}${hash}`;
}

/**
 * GAL-2: forgets every `orvano:v1:*` item the gallery stores (theme, density and sidebar, the table list's width,
 * palette recents) through the storage module (STO-1), then reloads.
 */
export function resetGallery(ids: { userId: string; projectId: string }) {
  themePreference.remove(undefined);
  layout.remove({ userId: ids.userId });
  paletteRecents.remove({ userId: ids.userId });
  resourceListLayout.remove({ ...ids, service: "databases" });
  window.location.reload();
}
