import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Navigate, createFileRoute, useNavigate } from '@tanstack/react-router'

import { Skeleton } from '@/components/ui/skeleton'
import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { orgsQuery, setupQuery } from '@/lib/queries'
import { ErrorPanel } from '@/shell/error-panel'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'

import { SetupForm } from './-auth/auth-form'

/**
 * The installer's setup link, `/setup#<token>` (spec 0006, AC-23). The token is read from the
 * fragment and the fragment removed from the address bar and history before anything renders, so
 * it never lingers there or reaches a server; it lives only in this route's context.
 */
export const Route = createFileRoute('/setup')({
  beforeLoad: () => {
    const token = window.location.hash.slice(1)
    if (token !== '') {
      window.history.replaceState(
        window.history.state,
        '',
        window.location.pathname + window.location.search,
      )
    }
    return { setupToken: token === '' ? undefined : token }
  },
  component: Setup,
})

function Setup() {
  usePageTitle('Set up Orvano')
  const { setupToken } = Route.useRouteContext()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const setup = useQuery(setupQuery())

  if (setup.data?.setupRequired === false) return <Navigate to="/sign-in" replace />

  return (
    <main id="main" className="mx-auto flex max-w-md flex-col gap-6 px-(--page-px) py-24">
      <LogoMark className="size-10 text-primary" />
      <PageHeading>Create the first admin</PageHeading>
      {setup.isError ? (
        <ErrorPanel
          error={setup.error}
          onRetry={() => {
            void setup.refetch()
          }}
        />
      ) : setup.isPending ? (
        <Skeleton aria-hidden className="h-64 w-full" />
      ) : (
        <>
          <p className="text-muted-foreground">
            This account runs the install: it can change its settings and invite others.
          </p>
          <SetupForm
            onSubmit={async ({ name, email, password }) => {
              await consoleApi().consoleAccount.create({
                email,
                password,
                name: name === '' ? null : name,
                setupToken: setupToken ?? null,
              })
              queryClient.clear()
              // You land signed in on your new personal org, your only org so far.
              const orgs = await queryClient.infiniteQuery(orgsQuery())
              const personal = orgs.pages.at(0)?.items.at(0)
              await (personal === undefined
                ? navigate({ to: '/orgs', replace: true })
                : navigate({ to: '/orgs/$orgId', params: { orgId: personal.id }, replace: true }))
            }}
          />
        </>
      )}
    </main>
  )
}
