import { createRootRouteWithContext, Outlet } from "@tanstack/react-router";
import type { RouterContext } from "@/router";
import { UnsavedChangesGuard } from "@/ui/form/unsaved-changes-guard";
import { Announcer } from "@/ui/announcer";
import { RootErrorPage } from "@/ui/error-page";
import { UiRouterProvider } from "@/ui/router-provider";
import { ToastRegion } from "@/ui/toast";

export const Route = createRootRouteWithContext<RouterContext>()({
  component: Root,
  errorComponent: RootErrorPage, // spec 0003 FLT-2
});

// Spec 0003 RTE-6: React Aria widgets navigate through the router. RTE-8: the one unsaved changes guard. Spec 0001
// UX-24, UX-41: the one toast region and the one announcer.
function Root() {
  return (
    <UiRouterProvider>
      <Outlet />
      <UnsavedChangesGuard />
      <ToastRegion />
      <Announcer />
    </UiRouterProvider>
  );
}
