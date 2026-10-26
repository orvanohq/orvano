import type { ChangeEvent, ReactNode } from "react";
import { Link, Outlet, useLocation } from "@tanstack/react-router";
import {
  setThemePreference,
  useThemePreference,
  type ThemePreference,
} from "@/shared/stores/theme";
import { setDensity, useLayout, type Density } from "@/shared/stores/layout";
import { user } from "./fixtures";
import { frameUrl, settingsStore, useSettings, type GallerySettings } from "./settings";

const update = (key: keyof GallerySettings) => (event: ChangeEvent<HTMLSelectElement>) => {
  settingsStore.set((state) => ({ ...state, [key]: event.target.value }));
};

/** The gallery page: the toolbar, then the scene (or, at a narrow width, the scene in a frame of that width). */
export function GalleryFrame() {
  const settings = useSettings();
  const theme = useThemePreference();
  const location = useLocation();
  const density = useLayout(user.id, (layout) => layout.density);

  return (
    <div className="flex h-full flex-col">
      <div
        role="toolbar"
        aria-label="Gallery settings"
        className="flex shrink-0 flex-wrap items-center gap-4 border-b bg-bg-muted px-4 py-2 text-sm"
      >
        <Link to="/" className="font-semibold text-accent-text">
          Gallery
        </Link>
        <Select
          label="Theme"
          value={theme}
          onChange={(event) => {
            setThemePreference(event.target.value as ThemePreference);
          }}
        >
          <option value="system">System</option>
          <option value="light">Light</option>
          <option value="dark">Dark</option>
        </Select>
        <Select
          label="Density"
          value={density}
          onChange={(event) => {
            setDensity(user.id, event.target.value as Density);
          }}
        >
          <option value="comfortable">Comfortable</option>
          <option value="compact">Compact</option>
        </Select>
        <Select label="Locale" value={settings.locale} onChange={update("locale")}>
          <option value="en">English</option>
          <option value="pseudo-LOCALE">Pseudo</option>
        </Select>
        <Select label="Direction" value={settings.direction} onChange={update("direction")}>
          <option value="ltr">Left to right</option>
          <option value="rtl">Right to left (ar-XB)</option>
        </Select>
        <Select label="Width" value={settings.width} onChange={update("width")}>
          <option value="full">Full</option>
          <option value="800">800 px (narrow)</option>
          <option value="360">360 px (phone)</option>
        </Select>
      </div>
      <div className="min-h-0 flex-1 overflow-auto">
        {settings.width === "full" ? (
          <Outlet />
        ) : (
          <div className="flex h-full justify-center bg-bg-muted p-4">
            {/* oxlint-disable-next-line react/iframe-missing-sandbox -- the dev only frame runs the gallery itself, so it needs scripts and same origin storage */}
            <iframe
              key={frameUrl(settings, location.href)}
              title={`Scene at ${settings.width} px`}
              src={frameUrl(settings, `#${location.href}`)}
              width={settings.width}
              className="h-full shrink-0 border bg-bg"
            />
          </div>
        )}
      </div>
    </div>
  );
}

function Select({
  label,
  value,
  onChange,
  children,
}: {
  label: string;
  value: string;
  onChange: (event: ChangeEvent<HTMLSelectElement>) => void;
  children: ReactNode;
}) {
  return (
    <label className="flex items-center gap-2">
      <span className="font-medium">{label}</span>
      <select
        value={value}
        onChange={onChange}
        className="h-control-sm rounded-md border border-border-strong bg-bg px-2 text-fg"
      >
        {children}
      </select>
    </label>
  );
}
