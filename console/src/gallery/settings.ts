import { createStore, useStore } from "@/shared/stores/create-store";

// The gallery toolbar's settings (theme and density live in the real stores, so the frame shares them through
// storage). A frame (the narrow width preview) reads
// them from its URL, so the outer gallery and the frame agree.

export type GalleryLocale = "en" | "pseudo-LOCALE";
export type Direction = "ltr" | "rtl";
export type Width = "full" | "800" | "360";

export interface GallerySettings {
  locale: GalleryLocale;
  direction: Direction;
  width: Width;
}

const query = new URLSearchParams(window.location.search);

/** True inside the narrow width frame, which renders the scene without the toolbar. */
export const inFrame = query.has("frame");

export const settingsStore = createStore<GallerySettings>({
  locale: query.get("locale") === "pseudo-LOCALE" ? "pseudo-LOCALE" : "en",
  direction: query.get("direction") === "rtl" ? "rtl" : "ltr",
  width: "full",
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
  });
  return `${window.location.pathname}?${params.toString()}${hash}`;
}
