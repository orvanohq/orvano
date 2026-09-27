import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { keys, projectQuery, signingKeysQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'

import { SigningKeysPanel } from './-users/parts'

export const Route = createFileRoute('/_app/projects/$projectId/settings')({
  component: SettingsPage,
})

/** Project settings; for now the token signing keys (spec 0004, AC-22). */
function SettingsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Settings', project?.name)
  const role = useOrgRole()
  const queryClient = useQueryClient()
  const signingKeys = useQuery(signingKeysQuery(projectId))

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <PageHeading>Settings</PageHeading>
      <SigningKeysPanel
        keys={signingKeys.data?.keys ?? []}
        loading={signingKeys.isPending}
        error={signingKeys.isError ? signingKeys.error : undefined}
        onRetry={() => {
          void signingKeys.refetch()
        }}
        rotateReason={meetsRole(role, 'owner') ? undefined : roleReason('owner')}
        onRotate={async () => {
          try {
            const rotated = await projectClient(projectId).consoleAuthKeys.rotate()
            queryClient.setQueryData(keys.signingKeys(projectId), rotated)
            notifySuccess('Signing key rotated')
          } catch (error) {
            notifyError("Couldn't rotate the key", error)
          }
        }}
      />
    </div>
  )
}
