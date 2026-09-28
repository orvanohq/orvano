// First: configures Zod before any imported module builds a schema.
import '@/lib/zod-config'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createRouter } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { PreferencesProvider } from '@/lib/preferences'
import { bindSession, isSessionError, redirectToSignIn } from '@/lib/session'
import { focusPageTitleOnNavigate } from '@/shell/focus'
import { NotFoundPage } from '@/shell/not-found'
import { RouteError } from '@/shell/route-error'
import { routeTree } from './routeTree.gen'

// One place turns a "no console session" answer into the sign in redirect (spec 0005, AC-20).
const onError = (error: unknown) => {
  if (isSessionError(error)) redirectToSignIn()
}

// The client already retries safe calls (spec 0001), so queries do not retry again.
const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
})
const router = createRouter({
  routeTree,
  context: { queryClient },
  defaultPreload: 'intent',
  defaultNotFoundComponent: NotFoundPage,
  defaultErrorComponent: RouteError,
})
bindSession(router, queryClient)
focusPageTitleOnNavigate(router)

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

const rootElement = document.getElementById('root')
if (!rootElement) throw new Error('index.html has no #root element')

createRoot(rootElement).render(
  <StrictMode>
    <PreferencesProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </PreferencesProvider>
  </StrictMode>,
)
