import { useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { useRouterState } from "@tanstack/react-router";
import { updateLayout, useLayout } from "@/shared/stores/layout";
import { Button, IconButton } from "../button";
import { SidebarOpenIcon } from "../icons";
import { AccountMenu } from "./account-menu";
import { BrandLink } from "./brand-mark";
import { Breadcrumbs } from "./breadcrumbs";
import { Drawer } from "./drawer";
import { PrimarySidebar, useScopeLabel } from "./primary-sidebar";
import { ShellContext } from "./shell-context";
import { SkipLink } from "./skip-link";
import type { AppShellProps } from "./types";
import { useDocumentTitle } from "./use-document-title";
import { useIsWide } from "./use-is-wide";

/**
 * Spec 0015 SHL-3: the frame every signed in screen sits in. Landmarks in order: the skip link, the `header` top bar
 * with the "Breadcrumb" `nav`, the scope's primary `nav`, an optional resource list (a `ResourceLayout`), and one
 * `main`. From 64rem the sidebar sits beside the content and collapses to the icon rail; below, the sidebar and the
 * resource list are drawers, which close on navigation and when the window widens (SHL-6).
 */
export function AppShell({ model, crumbs, resourceList, children }: AppShellProps) {
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

  return (
    <div className="grid h-full grid-rows-[auto_minmax(0,1fr)] bg-bg">
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
          <AccountMenu user={model.user} onSignOut={model.onSignOut} />
        </div>
      </header>
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
            className="min-w-0 flex-1 overflow-auto px-4 py-6 outline-none lg:px-6"
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
      </div>
    </div>
  );
}
