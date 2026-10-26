import type { ReactNode } from "react";
import { useRouter, type NavigateOptions } from "@tanstack/react-router";
import { RouterProvider } from "react-aria-components";

// Spec 0003 RTE-6: React Aria widgets navigate through the router. Without this provider React Aria leaves a
// keyboard Enter on a link to the browser, which reloads the page instead of routing.

declare module "react-aria-components" {
  interface RouterConfig {
    /** The TanStack options of the link that was pressed (`Link` passes them), so state and replace survive. */
    routerOptions: NavigateOptions;
  }
}

/** Turns a React Aria href into a router href. The gallery's hash history keeps the route after `#`. */
function routerHref(href: string): string {
  const url = new URL(href, window.location.href);
  return url.hash.startsWith("#/") ? url.hash.slice(1) : `${url.pathname}${url.search}${url.hash}`;
}

/** Rendered by the root route (and the gallery's), inside TanStack's RouterProvider. */
export function UiRouterProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  return (
    <RouterProvider
      navigate={(href, options) => {
        void router.navigate(options ?? { href: routerHref(href) });
      }}
      useHref={(href) => href}
    >
      {children}
    </RouterProvider>
  );
}
