import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Skeleton } from '@/components/ui/skeleton'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { authProvidersQuery, keys, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { providerInfo } from '@/auth/providers'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import { ErrorPanel } from '@/shell/error-panel'
import type { OAuthProvider } from '@orvano/console-client'

import { ProviderCard } from './-sign-in/provider-card'
import { ProviderDialog } from './-sign-in/provider-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/sign-in-methods')({
  component: SignInMethodsPage,
})

/**
 * The project's sign in providers (spec 0012, AC-25): Google, Apple, GitHub, and Microsoft as
 * cards with their state and readiness, each opening its settings. Owners and developers change
 * them; viewers see everything, disabled.
 */
function SignInMethodsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Sign in methods', project?.name)
  const role = useOrgRole()
  const readOnlyReason = meetsRole(role, 'developer') ? undefined : roleReason('developer')
  const queryClient = useQueryClient()
  const list = useQuery(authProvidersQuery(projectId))
  const [opened, setOpened] = useState<OAuthProvider | null>(null)
  const [open, setOpen] = useState(false)

  const items = list.data?.items ?? []
  const current = items.find((item) => item.provider === opened)

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading>Sign in methods</PageHeading>
        <p className="text-muted-foreground">
          Let your users sign in with their Google, Apple, GitHub, or Microsoft account.
        </p>
      </div>
      {list.isError ? (
        <ErrorPanel
          error={list.error}
          onRetry={() => {
            void list.refetch()
          }}
        />
      ) : list.isPending ? (
        <div className="grid gap-4 sm:grid-cols-2">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-36" />
          ))}
        </div>
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2" aria-label="Providers">
          {items.map((settings) => (
            <li key={settings.provider}>
              <ProviderCard
                settings={settings}
                onOpen={() => {
                  setOpened(settings.provider)
                  setOpen(true)
                }}
              />
            </li>
          ))}
        </ul>
      )}
      {current === undefined ? null : (
        <ProviderDialog
          // A fresh form for each provider, and again once a save changed it.
          key={`${current.provider}:${current.updatedAt ?? 'new'}`}
          settings={current}
          open={open}
          onOpenChange={setOpen}
          readOnlyReason={readOnlyReason}
          onSave={async (request) => {
            const saved = await projectClient(projectId).consoleAuthProviders.update(
              current.provider,
              request,
            )
            await queryClient.invalidateQueries({ queryKey: keys.authProviders(projectId) })
            notifySuccess(
              `${providerInfo(saved.provider).label} saved`,
              saved.enabled ? 'On' : 'Off',
            )
          }}
        />
      )}
    </div>
  )
}
