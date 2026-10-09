import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

import { Skeleton } from '@/components/ui/skeleton'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { authPoliciesQuery, keys, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { ErrorPanel } from '@/shell/error-panel'
import { PageHeading } from '@/shell/page-heading'
import type { UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { AppServersCard } from './-security/app-servers-card'
import { PasswordsCard } from './-security/passwords-card'
import { RateLimitsCard } from './-security/rate-limits-card'
import { SessionsCard } from './-security/sessions-card'

export const Route = createFileRoute('/_app/projects/$projectId/security')({
  component: SecurityPage,
})

/**
 * The project's auth rules (spec 0014, AC-34): one card per group of rules, each saving only its
 * own fields. Owners and developers change them; viewers see every value with the controls
 * disabled.
 */
function SecurityPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Security', project?.name)
  const role = useOrgRole()
  const readOnlyReason = meetsRole(role, 'developer') ? undefined : roleReason('developer')
  const queryClient = useQueryClient()
  const policies = useQuery(authPoliciesQuery(projectId))

  const save = (title: string) => async (request: UpdateAuthPoliciesRequest) => {
    const saved = await projectClient(projectId).consoleAuthPolicies.update(request)
    queryClient.setQueryData(keys.authPolicies(projectId), saved)
    notifySuccess(`${title} saved`)
  }

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading>Security</PageHeading>
        <p className="text-muted-foreground">
          Rules for how people sign up and sign in to this project, and how hard guessing gets.
        </p>
      </div>
      {policies.isError ? (
        <ErrorPanel
          error={policies.error}
          onRetry={() => {
            void policies.refetch()
          }}
        />
      ) : policies.isPending ? (
        <div className="flex flex-col gap-6">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-48" />
          ))}
        </div>
      ) : (
        <>
          <PasswordsCard
            // A fresh form once a save changed this card's values, and only then.
            key={JSON.stringify([
              policies.data.passwordMinLength,
              policies.data.passwordCommonCheck,
              policies.data.passwordBreachedCheck,
            ])}
            policies={policies.data}
            readOnlyReason={readOnlyReason}
            onSave={save('Passwords')}
          />
          <SessionsCard
            key={JSON.stringify([
              policies.data.accessTokenSeconds,
              policies.data.sessionIdleSeconds,
              policies.data.sessionAbsoluteSeconds,
              policies.data.maxSessionsPerUser,
            ])}
            policies={policies.data}
            readOnlyReason={readOnlyReason}
            onSave={save('Sessions')}
          />
          <AppServersCard
            key={JSON.stringify(policies.data.trustedServerCidrs)}
            policies={policies.data}
            readOnlyReason={readOnlyReason}
            onSave={save('App servers')}
          />
          <RateLimitsCard
            key={JSON.stringify([
              policies.data.signInFailedPerEmailIp,
              policies.data.signInFailedPerIp,
              policies.data.signUpPerIp,
              policies.data.anonymousPerIp,
              policies.data.emailSendPerIp,
            ])}
            policies={policies.data}
            readOnlyReason={readOnlyReason}
            onSave={save('Rate limits')}
          />
        </>
      )}
    </div>
  )
}
