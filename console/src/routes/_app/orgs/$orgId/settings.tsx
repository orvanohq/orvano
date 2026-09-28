import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

import { Button } from '@/components/ui/button'
import { CopyableId } from '@/components/ui/code-block'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { consoleApi } from '@/lib/console-client'
import { formatDate, formatFull } from '@/lib/format'
import { usePageTitle } from '@/lib/page-title'
import { orgProjectsQuery, orgQuery } from '@/lib/queries'
import { roleReason } from '@/lib/roles'
import { useStateMoved } from '@/lib/state-moved'
import { notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import { RenameForm } from '@/shell/rename-form'
import { DangerAction, SettingsSection } from '@/shell/settings-section'

import { applyOrg, deleteOrgReason, RestoreOrgButton } from './-org-settings/org-actions'

export const Route = createFileRoute('/_app/orgs/$orgId/settings')({
  component: OrgSettingsPage,
})

/**
 * Org settings (spec 0007, AC-2 to AC-5): the name, ID, and created date, and the Danger zone.
 * Owners only; anyone else who opens the URL sees it read only, every action disabled with the
 * reason. The sidebar shows the entry to owners only.
 */
function OrgSettingsPage() {
  const { orgId } = Route.useParams()
  const org = useQuery(orgQuery(orgId)).data
  usePageTitle('Settings', org?.name)
  const queryClient = useQueryClient()
  const stateMoved = useStateMoved()
  const projects = useInfiniteQuery(orgProjectsQuery(orgId, 25))
  if (org === undefined) return null
  const owner = meetsRole(org.role, 'owner')
  const deleting = org.status === 'deleting'
  const firstPage = projects.isError ? [] : projects.data?.pages[0]?.items

  return (
    <>
      <PageHeading>Settings</PageHeading>
      <SettingsSection title="General">
        <RenameForm
          // A fresh form when the name changes elsewhere (a teammate, another tab).
          key={org.name}
          id="org"
          name={org.name}
          disabledReason={
            !owner ? roleReason('owner') : deleting ? 'Restore the org to rename it' : undefined
          }
          errorTitle="Couldn't rename the org"
          onRename={async (name) => {
            try {
              const renamed = await consoleApi().consoleOrgs.update(orgId, { name })
              await applyOrg(queryClient, renamed)
              notifySuccess('Org renamed', renamed.name)
            } catch (error) {
              stateMoved("Couldn't rename the org", error, { orgId })
              throw error
            }
          }}
        />
        <dl className="grid max-w-xl grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
          <dt className="text-muted-foreground">Org ID</dt>
          <dd>
            <CopyableId value={org.id} label="org ID" />
          </dd>
          <dt className="text-muted-foreground">Created</dt>
          <dd>
            <time dateTime={org.createdAt} title={formatFull(org.createdAt)}>
              {formatDate(org.createdAt)}
            </time>
          </dd>
        </dl>
      </SettingsSection>
      <SettingsSection title="Danger zone" tone="danger">
        {deleting ? (
          <DangerAction
            title="Restore org"
            description="The org comes back as it was. Its projects stay deleted until you restore each one."
          >
            <RestoreOrgButton org={org} />
          </DangerAction>
        ) : (
          <DangerAction
            title="Delete org"
            description="Only an org with no live projects can be deleted. You can restore it until it is purged."
          >
            <ConfirmDialog
              trigger={
                <Button variant="destructive" disabledReason={deleteOrgReason(org, firstPage)}>
                  Delete org
                </Button>
              }
              title={`Delete ${org.name}?`}
              description={`${org.name} is deleted now and purged for good later. You can restore it until then.`}
              confirmLabel="Delete org"
              destructive
              onConfirm={async () => {
                try {
                  const deleted = await consoleApi().consoleOrgs.delete(orgId)
                  await applyOrg(queryClient, deleted)
                  notifySuccess('Org deleted', deleted.name)
                } catch (error) {
                  // `org_not_empty` stays in the dialog's alert (AC-4).
                  stateMoved("Couldn't delete the org", error, { orgId })
                  throw error
                }
              }}
            />
          </DangerAction>
        )}
      </SettingsSection>
    </>
  )
}
