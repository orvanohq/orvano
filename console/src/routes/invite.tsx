import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'
import { useState } from 'react'

import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { invalidateOrgLists, keys, optionalAccountQuery } from '@/lib/queries'
import { notifySuccess } from '@/lib/toast'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'
import type { InvitationPreview, MfaChallenge } from '@orvano/console-client'

import { InviteView, type InviteState } from './-invite/invite-view'

/**
 * The invite link, `/invite#<token>` (spec 0008, AC-20). As on `/setup`, the token is read from the
 * fragment and the fragment removed from the address bar and history before anything renders, so it
 * never lingers there or reaches a server. It lives only in this module and the route context; no
 * query key, cache entry, or URL carries it. The router loads the route again after the fragment is
 * removed, and that second `beforeLoad` keeps the token read first. A new link pasted into an open
 * tab (a fragment only change) bumps `tokenGeneration`, which the preview key carries instead of the
 * token, so the page previews the new link.
 */
let capturedToken: string | undefined
let tokenGeneration = 0

export const Route = createFileRoute('/invite')({
  beforeLoad: () => {
    const token = window.location.hash.slice(1)
    if (token !== '') {
      if (token !== capturedToken) tokenGeneration += 1
      capturedToken = token
      window.history.replaceState(
        window.history.state,
        '',
        window.location.pathname + window.location.search,
      )
    }
    return { inviteToken: capturedToken, inviteGeneration: tokenGeneration }
  },
  component: InvitePage,
})

/**
 * Previews the invitation, then lets you join with the account you are signed in to (AC-21), or
 * create an account or sign in when you are signed out (AC-22); an account with MFA passes its
 * second step in place first (spec 0013, AC-41). A 401 on the account query means "signed out"
 * here and never redirects to sign in.
 */
function InvitePage() {
  const { inviteToken, inviteGeneration } = Route.useRouteContext()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  // Bumped after the cache is cleared in place (sign in, sign out), so the queries load again.
  const [, setEpoch] = useState(0)
  // A sign in waiting for its second step (spec 0013, AC-41), and why one started over.
  const [mfa, setMfa] = useState<MfaChallenge | null>(null)
  const [notice, setNotice] = useState<string | undefined>(undefined)

  const preview = useQuery({
    queryKey: [...keys.invitationPreview, inviteGeneration],
    queryFn: ({ signal }) =>
      consoleApi().consoleInvitations.preview({ token: inviteToken ?? '' }, { signal }),
    enabled: inviteToken !== undefined,
    gcTime: 0,
    retry: false,
    meta: { sessionOptional: true },
  })
  const account = useQuery(optionalAccountQuery())
  const accept = useMutation({
    mutationFn: () => consoleApi().consoleInvitations.accept({ token: inviteToken ?? '' }),
    gcTime: 0,
  })
  usePageTitle(preview.data === undefined ? 'Invite' : `Join ${preview.data.orgName}`)

  const clearInPlace = () => {
    queryClient.clear()
    setEpoch((n) => n + 1)
  }

  const joined = async (orgId: string, message: string) => {
    notifySuccess(message)
    await invalidateOrgLists(queryClient)
    await navigate({ to: '/orgs/$orgId', params: { orgId }, replace: true })
  }

  const state = ((): InviteState => {
    if (inviteToken === undefined) return { kind: 'incomplete' }
    if (preview.isError) {
      const error = preview.error
      const status = error instanceof OrvanoError ? error.status : 0
      if (status === 404) return { kind: 'gone' }
      if (status === 410) return { kind: 'expired' }
      if (status === 409) return { kind: 'deleting' }
      return {
        kind: 'error',
        error,
        onRetry: () => {
          void preview.refetch()
        },
      }
    }
    if (preview.isPending || account.isPending) return { kind: 'loading' }
    const invitation: InvitationPreview = preview.data
    const me = account.data
    if ((me === null || me === undefined) && mfa !== null) {
      return {
        kind: 'mfa',
        preview: invitation,
        factors: mfa.factors,
        onSignedIn: () => {
          setMfa(null)
          clearInPlace()
          return Promise.resolve()
        },
        onStartOver: (reason) => {
          setNotice(reason)
          setMfa(null)
        },
      }
    }
    if (me === null || me === undefined) {
      return {
        kind: 'signed-out',
        preview: invitation,
        notice,
        onCreateAccount: async ({ name, email, password }) => {
          await consoleApi().consoleAccount.create({
            email,
            password,
            name: name === '' ? null : name,
            inviteToken,
          })
          queryClient.clear()
          await joined(invitation.orgId, `You joined ${invitation.orgName}`)
        },
        onSignIn: async (values) => {
          const result = await consoleApi().consoleAccount.createSession(values)
          setNotice(undefined)
          if (result.mfa !== null) {
            setMfa(result.mfa)
            return
          }
          clearInPlace()
        },
      }
    }
    if ((me.email ?? '').toLowerCase() !== invitation.email.toLowerCase()) {
      return {
        kind: 'mismatch',
        preview: invitation,
        accountEmail: me.email ?? '',
        onSignOut: async () => {
          await consoleApi().consoleAccount.deleteSession()
          clearInPlace()
        },
      }
    }
    return {
      kind: 'join',
      preview: invitation,
      onJoin: async () => {
        try {
          const result = await accept.mutateAsync()
          await joined(
            result.org.id,
            result.alreadyMember
              ? `You're already a member of ${result.org.name}`
              : `You joined ${result.org.name}`,
          )
        } finally {
          accept.reset()
        }
      },
    }
  })()

  return (
    <main id="main" className="mx-auto flex max-w-md flex-col gap-6 px-(--page-px) py-24">
      <LogoMark className="size-10 text-primary" />
      <PageHeading>
        {preview.data === undefined ? 'Invite' : `Join ${preview.data.orgName}`}
      </PageHeading>
      <InviteView state={state} />
    </main>
  )
}
