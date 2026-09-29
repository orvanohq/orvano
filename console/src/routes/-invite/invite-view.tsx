import { OrvanoError } from '@orvano/console-client'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { FormAlert } from '@/components/ui/form-alert'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { roleInfo } from '@/lib/roles'
import { ErrorPanel } from '@/shell/error-panel'
import type { InvitationPreview } from '@orvano/console-client'

import { authErrorMessage, SignInForm, SignUpForm } from '../-auth/auth-form'

/** What a new account from the invite sends. */
export interface NewAccount {
  name: string
  email: string
  password: string
}

/**
 * Every state of the invite page (spec 0008, AC-20 to AC-22). The route turns its queries into one
 * of these; the catalog renders each with stand ins.
 */
export type InviteState =
  | { kind: 'incomplete' }
  | { kind: 'loading' }
  | { kind: 'gone' }
  | { kind: 'expired' }
  | { kind: 'deleting' }
  | { kind: 'error'; error: unknown; onRetry: () => void }
  | { kind: 'join'; preview: InvitationPreview; onJoin: () => Promise<void> }
  | {
      kind: 'mismatch'
      preview: InvitationPreview
      accountEmail: string
      onSignOut: () => Promise<void>
    }
  | {
      kind: 'signed-out'
      preview: InvitationPreview
      onCreateAccount: (account: NewAccount) => Promise<void>
      onSignIn: (values: { email: string; password: string }) => Promise<void>
    }

/** "<inviter> invited you to join <org> as <role>", or without the inviter once they are gone. */
export function InviteLine({ preview }: { preview: InvitationPreview }) {
  const role = roleInfo[preview.role].label.toLowerCase()
  return (
    <p className="text-body">
      {preview.invitedByName === null ? (
        "You're invited"
      ) : (
        <>
          <strong>{preview.invitedByName}</strong> invited you
        </>
      )}{' '}
      to join <strong>{preview.orgName}</strong> as {role}.
    </p>
  )
}

/** The invite page's body in one state (AC-20 to AC-22). */
export function InviteView({ state }: { state: InviteState }) {
  switch (state.kind) {
    case 'incomplete':
      return (
        <FormAlert variant="warning" title="This invite link is incomplete">
          Open it again from the message you got.
        </FormAlert>
      )
    case 'loading':
      return (
        <div aria-busy className="flex flex-col gap-3">
          <Skeleton aria-hidden className="h-6 w-3/4" />
          <Skeleton aria-hidden className="h-40 w-full" />
        </div>
      )
    case 'gone':
      return (
        <FormAlert title="This invite link isn't valid anymore">
          It may have been used, replaced, or revoked.
        </FormAlert>
      )
    case 'expired':
      return (
        <FormAlert title="This invite expired">Ask an owner of the org for a new link.</FormAlert>
      )
    case 'deleting':
      return <FormAlert title="This org is being deleted">You can&apos;t join it now.</FormAlert>
    case 'error':
      return <ErrorPanel error={state.error} onRetry={state.onRetry} />
    case 'join':
      return <JoinPanel preview={state.preview} onJoin={state.onJoin} />
    case 'mismatch':
      return <MismatchPanel {...state} />
    case 'signed-out':
      return <SignedOutPanel {...state} />
  }
}

function JoinPanel({
  preview,
  onJoin,
}: {
  preview: InvitationPreview
  onJoin: () => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <div className="flex flex-col gap-4">
      <InviteLine preview={preview} />
      {error === null ? null : <FormAlert title="Couldn't join">{error}</FormAlert>}
      <Button
        loading={busy}
        className="self-start"
        onClick={() => {
          setBusy(true)
          setError(null)
          onJoin()
            .catch((failure: unknown) => {
              setError(authErrorMessage(failure))
            })
            .finally(() => {
              setBusy(false)
            })
        }}
      >
        Join
      </Button>
    </div>
  )
}

function MismatchPanel({
  preview,
  accountEmail,
  onSignOut,
}: {
  preview: InvitationPreview
  accountEmail: string
  onSignOut: () => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  return (
    <div className="flex flex-col gap-4">
      <InviteLine preview={preview} />
      <FormAlert variant="warning" title="This invite is for someone else">
        This invite is for <strong>{preview.email}</strong>. You&apos;re signed in as{' '}
        <strong>{accountEmail}</strong>.
      </FormAlert>
      <Button
        variant="outline"
        loading={busy}
        className="self-start"
        onClick={() => {
          setBusy(true)
          void onSignOut().finally(() => {
            setBusy(false)
          })
        }}
      >
        Sign out
      </Button>
    </div>
  )
}

function SignedOutPanel({
  preview,
  onCreateAccount,
  onSignIn,
}: {
  preview: InvitationPreview
  onCreateAccount: (account: NewAccount) => Promise<void>
  onSignIn: (values: { email: string; password: string }) => Promise<void>
}) {
  const [tab, setTab] = useState<'create' | 'sign-in'>('create')
  return (
    <div className="flex flex-col gap-4">
      <InviteLine preview={preview} />
      <Tabs
        value={tab}
        onValueChange={(next) => {
          setTab(next as 'create' | 'sign-in')
        }}
      >
        <TabsList className="w-full">
          <TabsTrigger value="create">Create account</TabsTrigger>
          <TabsTrigger value="sign-in">Sign in</TabsTrigger>
        </TabsList>
        <TabsContent value="create" className="pt-2">
          <SignUpForm
            id="invite-sign-up"
            email={{ value: preview.email, readOnly: true }}
            errorAction={(error) =>
              error instanceof OrvanoError && error.code === 'user_already_exists' ? (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setTab('sign-in')
                  }}
                >
                  Sign in instead
                </Button>
              ) : null
            }
            onSubmit={onCreateAccount}
          />
        </TabsContent>
        <TabsContent value="sign-in" className="pt-2">
          <SignInForm id="invite-sign-in" email={preview.email} onSubmit={onSignIn} />
        </TabsContent>
      </Tabs>
    </div>
  )
}
