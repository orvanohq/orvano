import type { ReactNode } from "react";
import { useLingui } from "@lingui/react";
import {
  createRootRouteWithContext,
  Outlet,
  type ErrorComponentProps,
} from "@tanstack/react-router";
import type { RouterContext } from "@/router";
import { UnsavedChangesGuard } from "@/ui/form/unsaved-changes-guard";
import { Announcer } from "@/ui/announcer";
import { RootErrorPage } from "@/ui/error-page";
import { LocaleProvider } from "@/ui/locale-provider";
import { UiRouterProvider } from "@/ui/router-provider";
import { ToastRegion } from "@/ui/toast";

export const Route = createRootRouteWithContext<RouterContext>()({
  component: Root,
  errorComponent: RootError, // spec 0003 FLT-2
});

// Spec 0003 RTE-6: React Aria widgets navigate through the router. RTE-8: the one unsaved changes guard. Spec 0001
// UX-24, UX-41: the one toast region and the one announcer.
function Root() {
  return (
    <ConsoleLocale>
      <UiRouterProvider>
        <Outlet />
        <UnsavedChangesGuard />
        <ToastRegion />
        <Announcer />
      </UiRouterProvider>
    </ConsoleLocale>
  );
}

function RootError({ error }: ErrorComponentProps) {
  return (
    <ConsoleLocale>
      <RootErrorPage error={error} />
    </ConsoleLocale>
  );
}

// Spec 0003 I18N-6: React Aria's strings, formats, and arrow keys follow the console's locale, not the browser's.
// Lingui's context changes when a locale is activated, so this re-renders with it.
function ConsoleLocale({ children }: { children: ReactNode }) {
  const { i18n } = useLingui();
  return <LocaleProvider locale={i18n.locale}>{children}</LocaleProvider>;
}
