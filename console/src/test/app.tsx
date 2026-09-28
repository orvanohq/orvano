import { render } from 'vitest-browser-react'

/*
 * Renders the whole console (router, query client, shell, toasts) at a URL, for page tests against
 * the fake API. Call `installFakeApi()` first: the app's modules load here, dynamically, so the
 * console client picks up the fake `fetch`.
 */

/** The theme and density the page renders in (spec 0005). */
export function setMode(theme: 'dark' | 'light', density: 'compact' | 'comfortable'): void {
  document.documentElement.dataset.theme = theme
  document.documentElement.dataset.density = density
  document.documentElement.style.colorScheme = theme
}

/** Renders the app at `at` and waits for the page heading. */
export async function renderApp(at: string) {
  const { QueryClient, QueryClientProvider } = await import('@tanstack/react-query')
  const { RouterProvider, createMemoryHistory, createRouter } =
    await import('@tanstack/react-router')
  const { routeTree } = await import('@/routeTree.gen')
  const { PreferencesProvider } = await import('@/lib/preferences')
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  const router = createRouter({
    routeTree,
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: [at] }),
  })
  const screen = await render(
    <PreferencesProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </PreferencesProvider>,
  )
  return { screen, router, queryClient }
}
