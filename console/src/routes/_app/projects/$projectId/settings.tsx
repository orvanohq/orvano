import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'

import { Button } from '@/components/ui/button'
import { CopyableId } from '@/components/ui/code-block'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { applyProject, runProjectAction } from '@/lib/project-actions'
import { keys, projectQuery, signingKeysQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { useStateMoved } from '@/lib/state-moved'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import { RenameForm } from '@/shell/rename-form'
import { DangerAction, SettingsSection } from '@/shell/settings-section'
import type { Project } from '@orvano/console-client'

import { SigningKeysPanel } from './-users/parts'

export const Route = createFileRoute('/_app/projects/$projectId/settings')({
  component: SettingsPage,
})

/**
 * Project settings (spec 0007, AC-7 and AC-8): the name and ID, the token signing keys (spec 0004,
 * AC-22), and the Danger zone. Owners and developers rename; only owners delete, after typing the
 * project's name.
 */
function SettingsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Settings', project?.name)
  const role = useOrgRole()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const stateMoved = useStateMoved()
  const signingKeys = useQuery(signingKeysQuery(projectId))
  if (project === undefined) return null
  const target = { projectId, orgId: project.orgId }

  // Runs from the toast, after you've left this page, so it relies on nothing mounted here (AC-8).
  const restoreFromToast = (deleted: Project) => {
    runProjectAction(queryClient, deleted.id, 'restore').then(
      () => {
        notifySuccess('Project restored', deleted.name)
      },
      (error: unknown) => {
        const title = "Couldn't restore the project"
        if (!stateMoved(title, error, target)) notifyError(title, error)
      },
    )
  }

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <PageHeading>Settings</PageHeading>
      <SettingsSection title="General">
        <RenameForm
          // A fresh form when the name changes elsewhere (a teammate, another tab).
          key={project.name}
          id="project"
          name={project.name}
          disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
          errorTitle="Couldn't rename the project"
          onRename={async (name) => {
            try {
              const renamed = await projectClient(projectId).consoleProjects.update({ name })
              await applyProject(queryClient, renamed)
              notifySuccess('Project renamed', renamed.name)
            } catch (error) {
              stateMoved("Couldn't rename the project", error, target)
              throw error
            }
          }}
        />
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">Project ID</span>
          <CopyableId value={project.id} label="project ID" />
        </div>
      </SettingsSection>
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
      <SettingsSection title="Danger zone" tone="danger">
        <DangerAction
          title="Delete project"
          description="Its users, keys, platforms, and data stop working at once. You can restore it until it is purged."
        >
          <ConfirmDialog
            trigger={
              <Button
                variant="destructive"
                disabledReason={meetsRole(role, 'owner') ? undefined : roleReason('owner')}
              >
                Delete project
              </Button>
            }
            title={`Delete ${project.name}?`}
            description="Apps using this project stop working at once. You can restore it until it is purged for good."
            confirmLabel="Delete project"
            destructive
            requireName={project.name}
            onConfirm={async () => {
              let deleted: Project
              try {
                deleted = await projectClient(projectId).consoleProjects.delete()
              } catch (error) {
                stateMoved("Couldn't delete the project", error, target)
                throw error
              }
              await navigate({ to: '/orgs/$orgId', params: { orgId: deleted.orgId } })
              await applyProject(queryClient, deleted)
              notifySuccess('Project deleted', deleted.name, {
                label: 'Restore',
                onClick: () => {
                  restoreFromToast(deleted)
                },
              })
            }}
          />
        </DangerAction>
      </SettingsSection>
    </div>
  )
}
