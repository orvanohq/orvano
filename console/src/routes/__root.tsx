import { createRootRouteWithContext, Outlet } from "@tanstack/react-router";
import type { RouterContext } from "@/router";
import { UiRouterProvider } from "@/ui/router-provider";

export const Route = createRootRouteWithContext<RouterContext>()({
  component: Root,
});

// Spec 0003 RTE-6: React Aria widgets navigate through the router.
function Root() {
  return (
    <UiRouterProvider>
      <Outlet />
    </UiRouterProvider>
  );
}
