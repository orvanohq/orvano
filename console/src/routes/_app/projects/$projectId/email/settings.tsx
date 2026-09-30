import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FormAlert } from '@/components/ui/form-alert'
import { Skeleton } from '@/components/ui/skeleton'
import { SmtpForm } from '@/email/smtp-form'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { keys, projectQuery, smtpQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { useStateMoved } from '@/lib/state-moved'
import { notifySuccess } from '@/lib/toast'
import { ErrorPanel } from '@/shell/error-panel'
import { meetsRole } from '@/shell/nav'
import { SettingsSection } from '@/shell/settings-section'
import type { EmailSender } from '@orvano/console-client'

export const Route = createFileRoute('/_app/projects/$projectId/email/settings')({
  component: EmailSettingsPage,
})

/** "Name <address>", or the address alone. */
function senderText(sender: EmailSender): string {
  return sender.name === null ? sender.email : `${sender.name} <${sender.email}>`
}

/**
 * The Email Settings tab (spec 0009, AC-1 to AC-6): which SMTP server the project sends through, the
 * form to give it its own, a test email, and "Stop using these settings". Owners and developers
 * change things; a viewer reads the same form.
 */
function EmailSettingsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Email settings', project?.name)
  const role = useOrgRole()
  const queryClient = useQueryClient()
  const stateMoved = useStateMoved()
  const smtp = useQuery(smtpQuery(projectId))
  const changeReason = meetsRole(role, 'developer') ? undefined : roleReason('developer')

  if (smtp.isError) {
    return (
      <ErrorPanel
        error={smtp.error}
        onRetry={() => {
          void smtp.refetch()
        }}
      />
    )
  }
  if (smtp.data === undefined) {
    return (
      <div aria-busy className="flex flex-col gap-4">
        <Skeleton aria-hidden className="h-12 w-full" />
        <Skeleton aria-hidden className="h-96 w-full" />
      </div>
    )
  }

  const { source, settings, installSender } = smtp.data
  const target = { projectId, orgId: project?.orgId }

  return (
    <div className="flex flex-col gap-4">
      {source === 'install' && installSender !== null ? (
        <FormAlert variant="info" title="Using this server’s email settings">
          Emails are sent as {senderText(installSender)}. Add your own SMTP server below to send
          from your domain.
        </FormAlert>
      ) : null}
      {source === 'none' ? (
        <FormAlert variant="warning" title="No email server is set up">
          Auth emails can’t be sent until you add one here or the install admin adds one for the
          whole server.
        </FormAlert>
      ) : null}
      <SettingsSection
        title="SMTP server"
        description="The mail server this project sends email through. The password is stored encrypted and never shown again."
      >
        <SmtpForm
          // A fresh form whenever the stored settings change: a save, a delete, or a teammate's edit.
          key={`${source}:${settings?.updatedAt ?? ''}`}
          id="smtp"
          settings={settings}
          readOnlyReason={changeReason}
          onSave={async (input) => {
            try {
              const saved = await projectClient(projectId).consoleSmtp.update(input)
              queryClient.setQueryData(keys.smtp(projectId), {
                source: 'project',
                settings: saved,
                installSender,
              })
              notifySuccess('Email settings saved', `Emails are sent as ${saved.fromEmail}.`)
            } catch (error) {
              stateMoved("Couldn't save the email settings", error, target)
              throw error
            }
          }}
          onTest={async (input) => {
            try {
              return await projectClient(projectId).consoleSmtp.test(input)
            } catch (error) {
              stateMoved("Couldn't send the test email", error, target)
              throw error
            }
          }}
          actions={
            source === 'project' ? (
              <ConfirmDialog
                trigger={
                  <Button variant="outline" disabledReason={changeReason}>
                    Stop using these settings
                  </Button>
                }
                title="Stop using these settings?"
                description={
                  installSender === null
                    ? 'This project won’t be able to send email.'
                    : 'Emails will use this server’s settings.'
                }
                confirmLabel="Stop using these settings"
                destructive
                onConfirm={async () => {
                  try {
                    await projectClient(projectId).consoleSmtp.delete()
                  } catch (error) {
                    stateMoved("Couldn't remove the email settings", error, target)
                    throw error
                  }
                  await queryClient.invalidateQueries({ queryKey: keys.smtp(projectId) })
                  notifySuccess(
                    'Email settings removed',
                    installSender === null
                      ? 'This project can’t send email now.'
                      : 'Emails use this server’s settings now.',
                  )
                }}
              />
            ) : undefined
          }
        />
      </SettingsSection>
    </div>
  )
}
