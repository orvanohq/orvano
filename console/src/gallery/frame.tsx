import { useEffect, useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Link, Outlet, useLocation, useRouter } from "@tanstack/react-router";
import {
  setThemePreference,
  useThemePreference,
  type ThemePreference,
} from "@/shared/stores/theme";
import { setDensity, useLayout, type Density } from "@/shared/stores/layout";
import { project, user } from "./fixtures";
import {
  frameUrl,
  resetGallery,
  settingsStore,
  useSettings,
  type GallerySettings,
} from "./settings";

const update = (key: keyof GallerySettings) => (event: ChangeEvent<HTMLSelectElement>) => {
  settingsStore.set((state) => ({ ...state, [key]: event.target.value }));
};

const control = "h-control-sm rounded-md border border-border-strong bg-bg px-2 text-fg";

/** The gallery page: the toolbar, then the scene (or, at a narrow width, the scene in a frame of that width). */
export function GalleryFrame() {
  const settings = useSettings();
  const theme = useThemePreference();
  const density = useLayout(user.id, (layout) => layout.density);
  const sceneRef = useRef<HTMLDivElement>(null);
  useScrollToTop(sceneRef);

  return (
    <div className="flex h-full flex-col">
      {/* A group, not a toolbar: native selects don't take the arrow key movement a toolbar promises (G-15). */}
      <fieldset className="grid shrink-0 grid-cols-2 gap-x-4 gap-y-2 border-b bg-bg-muted px-4 py-2 text-sm sm:flex sm:flex-wrap sm:items-center">
        <legend className="sr-only">Gallery settings</legend>
        {/* The toolbar comes first, so its skip link lets the scene's own first Tab (SRT-1) be checked. */}
        <button
          type="button"
          className="sr-only rounded-md bg-bg px-2 text-accent-text focus:not-sr-only"
          onClick={() => sceneRef.current?.focus()}
        >
          Skip to scene
        </button>
        <Link to="/" className="self-center font-semibold text-accent-text">
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
          <option value="pseudo-LOCALE">Pseudo (35% longer)</option>
        </Select>
        <Select label="Direction" value={settings.direction} onChange={update("direction")}>
          <option value="ltr">Left to right</option>
          <option value="rtl">Right to left (ar)</option>
        </Select>
        <Select label="Width" value={settings.width} onChange={update("width")}>
          <option value="full">Full</option>
          <option value="800">800 px (narrow)</option>
          <option value="360">360 px (phone)</option>
          <option value="320">320 px (smallest)</option>
        </Select>
        <Select label="Scenario" value={settings.scenario} onChange={update("scenario")}>
          <option value="normal">Normal</option>
          <option value="slow">Slow</option>
          <option value="fail">Fail</option>
          <option value="empty">Empty</option>
        </Select>
        <button
          type="button"
          className={`${control} self-end font-medium sm:self-auto`}
          onClick={() => {
            resetGallery({ userId: user.id, projectId: project.id });
          }}
        >
          Reset gallery
        </button>
      </fieldset>
      <div ref={sceneRef} tabIndex={-1} className="min-h-0 flex-1 overflow-auto outline-none">
        {settings.width === "full" ? <Outlet /> : <SceneFrame settings={settings} />}
      </div>
    </div>
  );
}

/** G-13: a new scene opens at its top, in the gallery's scroll container or (inside a frame) the window. */
export function useScrollToTop(container?: { current: HTMLElement | null }) {
  const router = useRouter();
  useEffect(
    () =>
      router.subscribe("onRendered", ({ pathChanged }) => {
        if (pathChanged) (container?.current ?? window).scrollTo({ top: 0 });
      }),
    [router, container],
  );
}

/**
 * The scene at a fixed width, in a frame of the same gallery. Navigating changes the frame's hash rather than
 * reloading it, so scroll, open overlays, and storage reads survive (G-16); a link followed inside the frame moves
 * the outer URL too. Only the settings the frame reads at start (locale, direction, scenario) reload it.
 */
function SceneFrame({ settings }: { settings: GallerySettings }) {
  const { locale, direction, scenario } = settings;
  return (
    <div className="flex h-full justify-center bg-bg-muted p-4">
      <SyncedFrame key={`${locale}|${direction}|${scenario}`} settings={settings} />
    </div>
  );
}

function SyncedFrame({ settings }: { settings: GallerySettings }) {
  const router = useRouter();
  const href = useLocation({ select: (location) => location.href });
  const ref = useRef<HTMLIFrameElement>(null);
  // The frame's first URL only; later navigations go through its hash.
  const [src] = useState(() => frameUrl(settings, `#${href}`));

  useEffect(() => {
    const frameWindow = ref.current?.contentWindow;
    if (frameWindow && frameWindow.location.hash !== `#${href}`) frameWindow.location.hash = href;
  }, [href]);

  useEffect(() => {
    const frame = ref.current;
    if (!frame) return;
    let frameWindow: Window | null = null;
    const follow = () => {
      const inner = frameWindow?.location.hash.slice(1);
      if (inner && inner !== router.history.location.href) router.history.push(inner);
    };
    const attach = () => {
      frameWindow?.removeEventListener("hashchange", follow);
      frameWindow = frame.contentWindow;
      frameWindow?.addEventListener("hashchange", follow);
    };
    frame.addEventListener("load", attach);
    return () => {
      frame.removeEventListener("load", attach);
      frameWindow?.removeEventListener("hashchange", follow);
    };
  }, [router]);

  return (
    // oxlint-disable-next-line react/iframe-missing-sandbox -- the dev only frame runs the gallery itself, so it needs scripts and same origin storage
    <iframe
      ref={ref}
      title={`Scene at ${settings.width} px`}
      src={src}
      width={settings.width}
      className="h-full shrink-0 border bg-bg"
    />
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
    <label className="flex min-w-0 flex-col gap-1 sm:flex-row sm:items-center sm:gap-2">
      <span className="font-medium">{label}</span>
      <select value={value} onChange={onChange} className={`${control} w-full sm:w-auto`}>
        {children}
      </select>
    </label>
  );
}
