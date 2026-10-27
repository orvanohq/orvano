import type { I18n, MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { setDensity } from "@/shared/stores/layout";
import { setThemePreference } from "@/shared/stores/theme";
import {
  DensityComfortableIcon,
  DensityCompactIcon,
  HelpIcon,
  ShortcutsIcon,
  ThemeDarkIcon,
  ThemeLightIcon,
  ThemeSystemIcon,
  type IconComponent,
} from "../icons";
import { navRegistry } from "./nav-registry";
import type { ShellLinkOptions, ShellModel } from "./types";

// Spec 0015 SHL-10: the command palette's local actions, a deliberate subset of spec 0001 UX-25 until feature 25 adds
// the create and copy actions and the server results. Built from the scope's nav, the theme and density setters,
// `ShortcutHelp`, and `DocsPane`.

export type PaletteSection = "navigation" | "theme" | "density" | "help";

export interface PaletteAction {
  /** Stable across sessions: the palette's recents store these (`orvano:v1:{userId}:palette`). */
  id: string;
  section: PaletteSection;
  /** The translated label, read in the active locale when the palette renders. */
  label: (i18n: I18n) => string;
  icon: IconComponent;
  run: PaletteRun;
}

/** What an action does. The palette runs it after it has closed and focus is back where it was. */
export type PaletteRun =
  | { kind: "navigate"; link: ShellLinkOptions }
  | { kind: "call"; call: () => void };

export interface PaletteHandlers {
  showShortcuts: () => void;
  openHelp: () => void;
}

export const paletteSectionLabels: Record<PaletteSection, MessageDescriptor> = {
  navigation: msg`Go to`,
  theme: msg`Theme`,
  density: msg`Density`,
  help: msg`Help`,
};

/** Every action for the current scope, in display order. */
export function paletteActions(model: ShellModel, handlers: PaletteHandlers): PaletteAction[] {
  const userId = model.user.id;
  return [
    ...model.nav.map((item): PaletteAction => ({
      id: `go:${item.id}`,
      section: "navigation",
      label: (i18n) => {
        const name = i18n._(navRegistry[item.id].label);
        return i18n._(msg`Go to ${name}`);
      },
      icon: navRegistry[item.id].icon,
      run: { kind: "navigate", link: item.link },
    })),
    {
      id: "theme:system",
      section: "theme",
      label: (i18n) => i18n._(msg`Use the system theme`),
      icon: ThemeSystemIcon,
      run: {
        kind: "call",
        call: () => {
          setThemePreference("system");
        },
      },
    },
    {
      id: "theme:light",
      section: "theme",
      label: (i18n) => i18n._(msg`Use the light theme`),
      icon: ThemeLightIcon,
      run: {
        kind: "call",
        call: () => {
          setThemePreference("light");
        },
      },
    },
    {
      id: "theme:dark",
      section: "theme",
      label: (i18n) => i18n._(msg`Use the dark theme`),
      icon: ThemeDarkIcon,
      run: {
        kind: "call",
        call: () => {
          setThemePreference("dark");
        },
      },
    },
    {
      id: "density:comfortable",
      section: "density",
      label: (i18n) => i18n._(msg`Use comfortable density`),
      icon: DensityComfortableIcon,
      run: {
        kind: "call",
        call: () => {
          setDensity(userId, "comfortable");
        },
      },
    },
    {
      id: "density:compact",
      section: "density",
      label: (i18n) => i18n._(msg`Use compact density`),
      icon: DensityCompactIcon,
      run: {
        kind: "call",
        call: () => {
          setDensity(userId, "compact");
        },
      },
    },
    {
      id: "help:shortcuts",
      section: "help",
      label: (i18n) => i18n._(msg`Show keyboard shortcuts`),
      icon: ShortcutsIcon,
      run: { kind: "call", call: handlers.showShortcuts },
    },
    {
      id: "help:docs",
      section: "help",
      label: (i18n) => i18n._(msg`Open help`),
      icon: HelpIcon,
      run: { kind: "call", call: handlers.openHelp },
    },
  ];
}
