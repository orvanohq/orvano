import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { describe, expect, it } from 'vitest'

import { Route as SignInRoute } from '@/routes/sign-in'
import { NotFoundPage } from '@/shell/not-found'
import { renderInRouter } from '@/test/router'

const paths = ['/orgs'] as const

describe('page titles outside the shell (AC-23)', () => {
  it('titles the 404 page', async () => {
    document.title = 'Orgs · Orvano'
    await renderInRouter(<NotFoundPage />, { at: '/', paths })
    expect(document.title).toBe('Page not found · Orvano')
  })

  it('titles the sign in page', async () => {
    document.title = 'Orgs · Orvano'
    const SignIn = SignInRoute.options.component
    if (SignIn === undefined) throw new Error('the sign in route has no component')
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await renderInRouter(
      <QueryClientProvider client={queryClient}>
        <SignIn />
      </QueryClientProvider>,
      { at: '/', paths },
    )
    expect(document.title).toBe('Sign in · Orvano')
  })
})
