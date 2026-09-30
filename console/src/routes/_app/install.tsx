import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FormAlert } from '@/components/ui/form-alert'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Skeleton } from '@/components/ui/skeleton'
import { EmailLogTable } from '@/email/email-log-table'
import { SmtpForm } from '@/email/smtp-form'
import { consoleApi } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import {
  accountQuery,
  installEmailsQuery,
  installSettingsQuery,
  installSmtpQuery,
  keys,
} from '@/lib/queries'
import { notifySuccess } from '@/lib/toast'
import { ErrorPanel } from '@/shell/error-panel'
import { InShellNotFound } from '@/shell/in-shell-not-found'
import { PageHeading } from '@/shell/page-heading'
import { SettingsSection } from '@/shell/settings-section'
import type { ConsoleSignupMode } from '@orvano/console-client'

export const Route = createFileRoute('/_app/install')({
  component: InstallPage,
})

const modes: readonly { value: ConsoleSignupMode; label: string; description: string }[] = [
  { value: 'invite', label: 'Invite only', description: 'People join through invite links.' },
  {
    value: 'open',
    label: 'Anyone can sign up',
    description: 'Anyone who can reach this console can create an account.',
  },
]

/**
 * Install settings, for install admins: whether console sign up is invite only or open (spec 0008,
 * AC-24), the email server of the whole install, and the console's own email log (spec 0009, AC-7
 * and AC-21). It sits in the same frame as `/orgs`, with no org or project sidebar. A skeleton shows until
 * your account loads; anyone who is not an install admin then sees the in shell not found screen.
 */
function InstallPage() {
  const account = useQuery(accountQuery())
  if (account.data === undefined) {
    return (
      <div aria-busy className="mx-auto flex max-w-4xl flex-col gap-6">
        <Skeleton aria-hidden className="h-8 w-48" />
        <Skeleton aria-hidden className="h-48 w-full" />
      </div>
    )
  }
  if (!account.data.isInstallAdmin) return <InShellNotFound what="page" />
  return <InstallSettings />
}

function InstallSettings() {
  usePageTitle('Install settings')
  const queryClient = useQueryClient()
  const settings = useQuery(installSettingsQuery())
  const [choice, setChoice] = useState<ConsoleSignupMode | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const current = settings.data?.consoleSignup
  const selected = choice ?? current

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6">
      <PageHeading>Install settings</PageHeading>
      {settings.isError ? (
        <ErrorPanel
          error={settings.error}
          onRetry={() => {
            void settings.refetch()
          }}
        />
      ) : selected === undefined ? (
        <Skeleton aria-hidden className="h-48 w-full" />
      ) : (
        <Card>
          <CardHeader>
            <CardTitle id="console-sign-up">Console sign up</CardTitle>
            <CardDescription>Who can create an account on this console.</CardDescription>
          </CardHeader>
          <CardContent>
            <form
              noValidate
              className="flex flex-col gap-(--stack)"
              onSubmit={(event) => {
                event.preventDefault()
                if (choice === null || choice === current) return
                setBusy(true)
                setError(null)
                consoleApi()
                  .consoleInstall.updateSettings({ consoleSignup: choice })
                  .then(
                    async (saved) => {
                      queryClient.setQueryData(keys.installSettings, saved)
                      setChoice(null)
                      await queryClient.invalidateQueries({ queryKey: keys.setup })
                      notifySuccess(
                        'Sign up updated',
                        saved.consoleSignup === 'open'
                          ? 'Anyone who can reach this console can sign up.'
                          : 'People join through invite links.',
                      )
                    },
                    (failure: unknown) => {
                      setError(describeError(failure).message)
                    },
                  )
                  .finally(() => {
                    setBusy(false)
                  })
              }}
            >
              {error === null ? null : <FormAlert title="Couldn't save">{error}</FormAlert>}
              <RadioGroup
                aria-labelledby="console-sign-up"
                value={selected}
                onValueChange={(next) => {
                  setChoice(next as ConsoleSignupMode)
                }}
              >
                {modes.map((mode) => {
                  const id = `console-sign-up-${mode.value}`
                  return (
                    <label
                      key={mode.value}
                      htmlFor={id}
                      className="flex cursor-pointer items-start gap-3 rounded-md border border-input p-3 has-data-checked:border-primary has-data-checked:bg-accent"
                    >
                      <RadioGroupItem
                        id={id}
                        value={mode.value}
                        aria-labelledby={`${id}-label`}
                        aria-describedby={`${id}-description`}
                        className="mt-0.5"
                      />
                      <span className="flex flex-col gap-0.5">
                        <span id={`${id}-label`} className="font-medium">
                          {mode.label}
                        </span>
                        <span id={`${id}-description`} className="text-small text-muted-foreground">
                          {mode.description}
                        </span>
                      </span>
                    </label>
                  )
                })}
              </RadioGroup>
              {selected === 'open' ? (
                <FormAlert variant="warning" title="Anyone can create an account">
                  Anyone who can reach this console can create an account and their own orgs.
                </FormAlert>
              ) : null}
              <Button
                type="submit"
                className="self-start"
                loading={busy}
                disabledReason={
                  choice === null || choice === current ? 'Choose a different setting' : undefined
                }
              >
                Save
              </Button>
            </form>
          </CardContent>
        </Card>
      )}
      <InstallEmailServer />
      <InstallEmails />
    </div>
  )
}

/**
 * The "Email server" card (spec 0009, AC-7): the SMTP server every project without its own sends
 * through, and the one that sends console invites. The same form as a project's Settings tab.
 */
function InstallEmailServer() {
  const queryClient = useQueryClient()
  const smtp = useQuery(installSmtpQuery())

  /** A project's Settings tab names who the install sends as, so those answers are stale now. */
  const refreshProjects = () =>
    queryClient.invalidateQueries({
      predicate: (query) => query.queryKey[1] === 'projects' && query.queryKey.at(-1) === 'smtp',
    })

  return (
    <SettingsSection
      title="Email server"
      description="The SMTP server this install sends email through: console invites, and the emails of every project that has no server of its own. The password is stored encrypted and never shown again."
    >
      {smtp.isError ? (
        <ErrorPanel
          error={smtp.error}
          onRetry={() => {
            void smtp.refetch()
          }}
        />
      ) : smtp.data === undefined ? (
        <Skeleton aria-hidden className="h-96 w-full" />
      ) : (
        <SmtpForm
          // A fresh form whenever the stored settings change: a save or a delete.
          key={smtp.data.settings?.updatedAt ?? 'none'}
          id="install-smtp"
          settings={smtp.data.settings}
          readOnlyReason={undefined}
          onSave={async (input) => {
            const saved = await consoleApi().consoleInstall.updateSmtp(input)
            queryClient.setQueryData(keys.installSmtp, { settings: saved })
            await refreshProjects()
            notifySuccess('Email server saved', `Emails are sent as ${saved.fromEmail}.`)
          }}
          onTest={(input) => consoleApi().consoleInstall.testSmtp(input)}
          actions={
            smtp.data.settings === null ? undefined : (
              <ConfirmDialog
                trigger={<Button variant="outline">Remove email server</Button>}
                title="Remove the email server?"
                description="Projects without their own settings won’t be able to send email."
                confirmLabel="Remove email server"
                destructive
                onConfirm={async () => {
                  await consoleApi().consoleInstall.deleteSmtp()
                  await queryClient.invalidateQueries({ queryKey: keys.installSmtp })
                  await refreshProjects()
                  notifySuccess('Email server removed', 'Invites are shared by link only now.')
                }}
              />
            )
          }
        />
      )}
    </SettingsSection>
  )
}

/** The "Console emails" card (spec 0009, AC-21): the invites this console sent, with their status. */
function InstallEmails() {
  const emails = useInfiniteQuery(installEmailsQuery())
  return (
    <SettingsSection
      title="Console emails"
      description="The invite emails this console sent in the last 30 days. Addresses are masked, and no content is kept."
    >
      <EmailLogTable label="Console emails" emails={emails} />
    </SettingsSection>
  )
}
