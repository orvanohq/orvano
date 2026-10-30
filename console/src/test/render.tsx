import { I18nProvider } from "@lingui/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";
import { render } from "vitest-browser-react";
import { consoleConfigQuery } from "@/features/console-config/api/queries";
import { createAppRouter } from "@/router";
import type { ConsoleConfig } from "@/shared/api/console-config";
import { UnsavedChangesGuard } from "@/ui/form/unsaved-changes-guard";
import { UiRouterProvider } from "@/ui/router-provider";
import { activateLocale, i18n } from "@/shared/i18n";

/**
 * Spec 0003 QA-3: renders the real route tree at an in app URL (`/`, not `/console/`: memory history does not apply
 * the router's base path), with a fresh query client that never retries. MSW handlers join here once the console
 * calls the API (QA-2). `config` is what boot loaded (RTE-10); by default setup is done, so pages render as themselves.
 */
export async function renderWithProviders(
  url: string,
  { config = { setupRequired: false } }: { config?: ConsoleConfig } = {},
) {
  // The boot step from main.tsx (RTE-10): I18nProvider renders nothing until a locale is active.
  if (!i18n.locale) await activateLocale("en");
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  queryClient.setQueryData(consoleConfigQuery().queryKey, config);
  const router = createAppRouter({
    queryClient,
    history: createMemoryHistory({ initialEntries: [url] }),
  });
  await router.load();
  const screen = await render(
    <I18nProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nProvider>,
  );
  return Object.assign(screen, { router });
}

/**
 * Renders a primitive inside a minimal router at `/` (links need one), with the `en` catalog. `routes` adds sibling
 * routes, for tests that navigate or preload.
 */
export async function renderInRouter(
  element: ReactNode,
  routes: { path: string; loader?: () => unknown }[] = [],
) {
  if (!i18n.locale) await activateLocale("en");
  const rootRoute = createRootRoute({
    component: () => (
      <UiRouterProvider>
        <Outlet />
        <UnsavedChangesGuard />
      </UiRouterProvider>
    ),
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({ getParentRoute: () => rootRoute, path: "/", component: () => element }),
      ...routes.map((route) =>
        createRoute({
          getParentRoute: () => rootRoute,
          path: route.path,
          loader: route.loader,
          component: () => null,
        }),
      ),
    ]),
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  await router.load();
  return {
    router,
    screen: await render(
      <I18nProvider i18n={i18n}>
        <RouterProvider router={router} />
      </I18nProvider>,
    ),
  };
}
