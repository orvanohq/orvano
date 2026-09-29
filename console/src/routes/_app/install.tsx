import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { FormAlert } from '@/components/ui/form-alert'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Skeleton } from '@/components/ui/skeleton'
import { consoleApi } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import { accountQuery, installSettingsQuery, keys } from '@/lib/queries'
import { notifySuccess } from '@/lib/toast'
import { ErrorPanel } from '@/shell/error-panel'
import { InShellNotFound } from '@/shell/in-shell-not-found'
import { PageHeading } from '@/shell/page-heading'
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
 * Install settings (spec 0008, AC-24), for install admins: whether console sign up is invite only or
 * open. It sits in the same frame as `/orgs`, with no org or project sidebar. A skeleton shows until
 * your account loads; anyone who is not an install admin then sees the in shell not found screen.
 */
function InstallPage() {
  const account = useQuery(accountQuery())
  if (account.data === undefined) {
    return (
      <div aria-busy className="mx-auto flex max-w-2xl flex-col gap-6">
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
    <div className="mx-auto flex max-w-2xl flex-col gap-6">
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
    </div>
  )
}
