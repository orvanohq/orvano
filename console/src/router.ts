import { createRouter, type RouterHistory } from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import type { ApiError } from "@/shared/api/errors";
import { config } from "@/shared/config";
import { routeTree } from "./routeTree.gen";

export interface RouterContext {
  queryClient: QueryClient;
}

// Spec 0003 RTE-3. The search serializer (URL-2), pending skeleton, and error and not found components come with
// shared/url and the design system (feature 11).
export function createAppRouter(opts: { queryClient: QueryClient; history?: RouterHistory }) {
  return createRouter({
    routeTree,
    history: opts.history,
    context: { queryClient: opts.queryClient },
    basepath: config.basePath,
    defaultPreload: "intent",
    defaultPreloadStaleTime: 0,
    defaultPendingMs: 300,
    defaultPendingMinMs: 500,
    scrollRestoration: true,
  });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}

// Spec 0003 QRY-4: every query and mutation fails with the client's ApiError (FLT-1), and a mutation's meta says where
// its error shows (FLT-3).
declare module "@tanstack/react-query" {
  interface Register {
    defaultError: ApiError;
    mutationMeta: { surface?: "toast" };
  }
}
