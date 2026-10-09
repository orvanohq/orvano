import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Skeleton } from '@/components/ui/skeleton'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import {
  authMethodsQuery,
  authProvidersQuery,
  keys,
  platformsQuery,
  projectQuery,
} from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifyError, notifySuccess } from '@/lib/toast'
import { providerInfo } from '@/auth/providers'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import { ErrorPanel } from '@/shell/error-panel'
import type {
  AuthMethodSettings,
  OAuthProvider,
  UpdateAuthMethodSettingsRequest,
} from '@orvano/console-client'

import { PasskeysCard, TotpCard } from './-sign-in/method-cards'
import { PasskeysDialog } from './-sign-in/passkeys-dialog'
import { ProviderCard } from './-sign-in/provider-card'
import { ProviderDialog } from './-sign-in/provider-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/sign-in-methods')({
  component: SignInMethodsPage,
})

/**
 * The project's sign in providers (spec 0012, AC-25): Google, Apple, GitHub, and Microsoft as
 * cards with their state and readiness, each opening its settings; and the Authenticator app and
 * Passkeys cards (spec 0013, AC-43). Owners and developers change them; viewers see everything,
 * disabled.
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

  const methods = useQuery(authMethodsQuery(projectId))
  const platforms = useInfiniteQuery(platformsQuery(projectId))
  const [passkeysOpen, setPasskeysOpen] = useState(false)

  const items = list.data?.items ?? []
  const current = items.find((item) => item.provider === opened)

  const saveMethods = async (request: UpdateAuthMethodSettingsRequest) => {
    const saved = await projectClient(projectId).consoleAuthMethods.update(request)
    // Spec 0014, AC-27: an update doesn't count users without MFA, so keep the last count and read
    // it again.
    queryClient.setQueryData<AuthMethodSettings>(keys.authMethods(projectId), (before) => ({
      ...saved,
      activeUsersWithoutMfa: saved.activeUsersWithoutMfa ?? before?.activeUsersWithoutMfa ?? null,
    }))
    void queryClient.invalidateQueries({ queryKey: keys.authMethods(projectId) })
    return saved
  }

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading>Sign in methods</PageHeading>
        <p className="text-muted-foreground">
          Let your users sign in with their Google, Apple, GitHub, or Microsoft account.
        </p>
      </div>
      <section className="flex flex-col gap-3" aria-labelledby="mfa-passkeys-heading">
        <h2 id="mfa-passkeys-heading" className="text-lg font-semibold">
          MFA and passkeys
        </h2>
        {methods.isError ? (
          <ErrorPanel
            error={methods.error}
            onRetry={() => {
              void methods.refetch()
            }}
          />
        ) : methods.isPending ? (
          <div className="grid gap-4 sm:grid-cols-2">
            {[0, 1].map((i) => (
              <Skeleton key={i} className="h-36" />
            ))}
          </div>
        ) : (
          <ul className="grid gap-4 sm:grid-cols-2" aria-label="MFA and passkeys">
            <li>
              <TotpCard
                projectId={projectId}
                settings={methods.data}
                readOnlyReason={readOnlyReason}
                onRequireMfa={async (required) => {
                  const saved = await saveMethods({ mfaRequired: required })
                  notifySuccess('Require MFA saved', saved.mfaRequired ? 'On' : 'Off')
                }}
                onToggle={async (enabled) => {
                  try {
                    const saved = await saveMethods({ totpEnabled: enabled })
                    notifySuccess('Authenticator app saved', saved.totpEnabled ? 'On' : 'Off')
                  } catch (error) {
                    notifyError("Couldn't save the authenticator app", error)
                  }
                }}
              />
            </li>
            <li>
              <PasskeysCard
                settings={methods.data}
                onOpen={() => {
                  setPasskeysOpen(true)
                }}
              />
            </li>
          </ul>
        )}
      </section>
      <h2 className="text-lg font-semibold">Providers</h2>
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
      {methods.data === undefined ? null : (
        <PasskeysDialog
          // A fresh form once a save changed the settings.
          key={JSON.stringify(methods.data)}
          settings={methods.data}
          projectName={project?.name ?? ''}
          platforms={platforms.data?.pages.flatMap((page) => page.items) ?? []}
          open={passkeysOpen}
          onOpenChange={setPasskeysOpen}
          readOnlyReason={readOnlyReason}
          onSave={async (request) => {
            const saved = await saveMethods(request)
            notifySuccess('Passkeys saved', saved.passkeysEnabled ? 'On' : 'Off')
          }}
        />
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
