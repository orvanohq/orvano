import { lazy, Suspense, useEffect, useEffectEvent, useRef, useState } from "react";
import { msg } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useRouterState } from "@tanstack/react-router";
import { updateLayout, useLayout } from "@/shared/stores/layout";
import { Button, IconButton } from "../button";
import { cx } from "../cx";
import { SidebarOpenIcon } from "../icons";
import { useDelayedPending } from "../skeleton";
import { useShortcut } from "../use-shortcut";
import { AccountMenu } from "./account-menu";
import { ActivityIndicator } from "./activity-indicator";
import { BrandLink } from "./brand-mark";
import { Breadcrumbs } from "./breadcrumbs";
import { Drawer } from "./drawer";
import { HelpButton } from "./help-button";
import { paletteActions } from "./palette-actions";
import { PALETTE_KEYS, PaletteButton } from "./palette-button";
import { PrimarySidebar, useScopeLabel } from "./primary-sidebar";
import { ShellContext } from "./shell-context";
import { SkipLink } from "./skip-link";
import type { AppShellProps } from "./types";
import { useDocumentTitle } from "./use-document-title";
import { useIsWide } from "./use-is-wide";

// PRM-10: the panels are not in the first load; their triggers (the palette button, `?`, the help button) are in the
// shell. The browser caches each import, so a preload and the first open share one request.
const loadPalette = () => import("../command-palette");
const loadShortcutHelp = () => import("../shortcut-help");
const loadDocsPane = () => import("../docs-pane");
const CommandPalette = lazy(() =>
  loadPalette().then((module) => ({ default: module.CommandPalette })),
);
const ShortcutHelp = lazy(() =>
  loadShortcutHelp().then((module) => ({ default: module.ShortcutHelp })),
);
const DocsPane = lazy(() => loadDocsPane().then((module) => ({ default: module.DocsPane })));

/** design.md _Lazy panels_: fetch a panel's code ahead of use. A failure here is ignored; opening tries again. */
function preload(load: () => Promise<unknown>): () => void {
  return () => {
    load().catch(() => undefined);
  };
}

/** Rendered while a lazy panel's code loads, so its button can show a busy state (design.md _Lazy panels_). */
function PanelLoading({ onLoadingChange }: { onLoadingChange: (loading: boolean) => void }) {
  const change = useEffectEvent(onLoadingChange);
  useEffect(() => {
    change(true);
    return () => {
      change(false);
    };
  }, []);
  return null;
}

const paletteShortcut = msg`Open the command palette`;
const shortcutHelpShortcut = msg`Show keyboard shortcuts`;

/**
 * Spec 0015 SHL-3: the frame every signed in screen sits in. Landmarks in order: the skip link, the `header` top bar
 * with the "Breadcrumb" `nav`, the scope's primary `nav`, an optional resource list (a `ResourceLayout`), and one
 * `main`. From 64rem the sidebar sits beside the content and collapses to the icon rail; below, the sidebar and the
 * resource list are drawers, which close on navigation and when the window widens (SHL-6). The top bar's inline end
 * holds the activity indicator, help (the docs pane, SHL-9), the command palette (Mod+K, SHL-10), and the account
 * menu (SHL-4); `?` opens the shortcut list (A11Y-7).
 */
export function AppShell({ model, crumbs, resourceList, banner, children }: AppShellProps) {
  const { t } = useLingui();
  const isWide = useIsWide();
  const collapsed = useLayout(model.user.id, (layout) => layout.sidebarCollapsed);
  const scopeLabel = useScopeLabel(model.scope);
  useDocumentTitle(crumbs, model);

  // A drawer is open at the location it was opened from, so navigating closes it.
  const href = useRouterState({ select: (state) => state.location.href });
  const [navOpenAt, setNavOpenAt] = useState<string | null>(null);
  const [listOpenAt, setListOpenAt] = useState<string | null>(null);
  // An open drawer closes when the window widens (adjusting state during render, not in an effect).
  const [wasWide, setWasWide] = useState(isWide);
  if (wasWide !== isWide) {
    setWasWide(isWide);
    if (isWide) {
      setNavOpenAt(null);
      setListOpenAt(null);
    }
  }
  const navOpen = !isWide && navOpenAt === href;
  const listOpen = !isWide && listOpenAt === href;

  // The power panels. Each mounts on first use and then stays, so it can animate out.
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [docsOpen, setDocsOpen] = useState(false);
  const [used, setUsed] = useState({ palette: false, shortcuts: false, docs: false });
  const [loading, setLoading] = useState({ palette: false, docs: false });
  // After 300 ms of loading, the panel's button shows that it is busy.
  const paletteBusy = useDelayedPending(paletteOpen && loading.palette);
  const docsBusy = useDelayedPending(docsOpen && loading.docs);
  const helpButton = useRef<HTMLButtonElement>(null);

  // The panels' code loads once the browser is idle, and sooner when the pointer or focus reaches their buttons.
  useEffect(() => {
    const preloadAll = () => {
      for (const load of [loadPalette, loadShortcutHelp, loadDocsPane]) preload(load)();
    };
    if (typeof window.requestIdleCallback !== "function") {
      const timer = setTimeout(preloadAll, 2000);
      return () => {
        clearTimeout(timer);
      };
    }
    const id = window.requestIdleCallback(preloadAll, { timeout: 5000 });
    return () => {
      window.cancelIdleCallback(id);
    };
  }, []);
  const openPalette = () => {
    setUsed((previous) => ({ ...previous, palette: true }));
    setPaletteOpen(true);
  };
  const openShortcuts = () => {
    setUsed((previous) => ({ ...previous, shortcuts: true }));
    setShortcutsOpen(true);
  };
  const toggleDocs = (open: boolean) => {
    setUsed((previous) => ({ ...previous, docs: true }));
    setDocsOpen(open);
  };
  const paletteKeys = useShortcut({
    id: "command-palette",
    keys: PALETTE_KEYS,
    label: paletteShortcut,
    onTrigger: openPalette,
  });
  useShortcut({
    id: "shortcut-help",
    keys: "?",
    label: shortcutHelpShortcut,
    onTrigger: openShortcuts,
  });

  return (
    <div
      className={cx(
        "grid h-full bg-bg",
        banner ? "grid-rows-[auto_auto_minmax(0,1fr)]" : "grid-rows-[auto_minmax(0,1fr)]",
      )}
    >
      <SkipLink />
      <header className="flex h-topbar min-w-0 items-center gap-2 border-b bg-bg-subtle px-2 lg:gap-4 lg:px-4">
        {!isWide && (
          <IconButton
            icon={SidebarOpenIcon}
            aria-label={t`Open navigation`}
            onPress={() => {
              setNavOpenAt(href);
            }}
          />
        )}
        <BrandLink markOnly={!isWide || collapsed} />
        <Breadcrumbs model={model} crumbs={crumbs} collapsed={!isWide} />
        <div className="ms-auto flex shrink-0 items-center gap-1">
          <ActivityIndicator />
          <span
            className="contents"
            onPointerEnter={preload(loadDocsPane)}
            onFocus={preload(loadDocsPane)}
          >
            <HelpButton
              ref={helpButton}
              isExpanded={docsOpen}
              isPending={docsBusy}
              onPress={() => {
                toggleDocs(!docsOpen);
              }}
            />
          </span>
          <span
            className="contents"
            onPointerEnter={preload(loadPalette)}
            onFocus={preload(loadPalette)}
          >
            <PaletteButton onPress={openPalette} isPending={paletteBusy} {...paletteKeys} />
          </span>
          {/* The account menu holds Keyboard shortcuts. */}
          <span
            className="contents"
            onPointerEnter={preload(loadShortcutHelp)}
            onFocus={preload(loadShortcutHelp)}
          >
            <AccountMenu
              user={model.user}
              onSignOut={model.onSignOut}
              onShowShortcuts={openShortcuts}
            />
          </span>
        </div>
      </header>
      {banner}
      <div className="flex min-h-0 min-w-0">
        {isWide ? (
          <PrimarySidebar
            scope={model.scope}
            nav={model.nav}
            mode={collapsed ? "rail" : "expanded"}
            onCollapsedChange={(next) => {
              updateLayout(model.user.id, { sidebarCollapsed: next });
            }}
          />
        ) : (
          <Drawer
            isOpen={navOpen}
            onOpenChange={(open) => {
              setNavOpenAt(open ? href : null);
            }}
            aria-label={scopeLabel}
          >
            <PrimarySidebar scope={model.scope} nav={model.nav} mode="drawer" />
          </Drawer>
        )}
        <div className="flex min-h-0 min-w-0 flex-1">
          {resourceList && (
            <ShellContext
              value={{
                userId: model.user.id,
                projectId: model.project?.id ?? "",
                isWide,
                listDrawerOpen: listOpen,
                setListDrawerOpen: (open) => {
                  setListOpenAt(open ? href : null);
                },
              }}
            >
              {resourceList}
            </ShellContext>
          )}
          <main
            id="main"
            tabIndex={-1}
            // SHL-8: the toast region publishes its height, so focus never lands under a toast.
            className="min-w-0 flex-1 scroll-pb-(--toast-inset) overflow-auto px-4 py-6 outline-none lg:px-6"
          >
            {resourceList && !isWide && (
              <div className="mb-4">
                <Button
                  size="sm"
                  onPress={() => {
                    setListOpenAt(href);
                  }}
                >
                  {t`Show list`}
                </Button>
              </div>
            )}
            {children}
          </main>
        </div>
        {used.docs && (
          <Suspense
            fallback={
              <PanelLoading
                onLoadingChange={(docs) => {
                  setLoading((previous) => ({ ...previous, docs }));
                }}
              />
            }
          >
            <DocsPane
              isOpen={docsOpen}
              isWide={isWide}
              onOpenChange={toggleDocs}
              onClosed={() => {
                // SHL-9: focus returns to the help button, once the pane (or its drawer) is gone.
                helpButton.current?.focus();
              }}
            />
          </Suspense>
        )}
      </div>
      {used.palette && (
        <Suspense
          fallback={
            <PanelLoading
              onLoadingChange={(palette) => {
                setLoading((previous) => ({ ...previous, palette }));
              }}
            />
          }
        >
          <CommandPalette
            isOpen={paletteOpen}
            onOpenChange={setPaletteOpen}
            userId={model.user.id}
            actions={paletteActions(model, {
              showShortcuts: openShortcuts,
              openHelp: () => {
                toggleDocs(true);
              },
            })}
          />
        </Suspense>
      )}
      {used.shortcuts && (
        <Suspense fallback={null}>
          <ShortcutHelp isOpen={shortcutsOpen} onOpenChange={setShortcutsOpen} />
        </Suspense>
      )}
    </div>
  );
}
