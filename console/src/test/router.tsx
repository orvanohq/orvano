import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { render } from 'vitest-browser-react'

/**
 * Renders `ui` inside a real router on an in memory history, so links resolve and navigation
 * works. `paths` are the routes that exist; each one renders nothing of its own.
 */
export async function renderInRouter(
  ui: ReactNode,
  { at, paths }: { at: string; paths: readonly string[] },
) {
  const root = createRootRoute({ component: () => <>{ui}</> })
  const routes = paths.map((path) => createRoute({ getParentRoute: () => root, path }))
  const router = createRouter({
    routeTree: root.addChildren(routes),
    history: createMemoryHistory({ initialEntries: [at] }),
  })
  // Load the first match before rendering, so the tree is never empty while the router starts.
  await router.load()
  const screen = await render(<RouterProvider router={router} />)
  return { router, screen }
}
