import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, createFileRoute, useNavigate } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'
import type { MfaChallenge } from '@orvano/console-client'
import { useEffect, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'
import { FormAlert } from '@/components/ui/form-alert'
import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { setupQuery } from '@/lib/queries'
import { safeRedirect } from '@/lib/redirect'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'

import { SignInForm } from './-auth/auth-form'
import { MfaStep } from './-auth/mfa-step'
import { passkeyErrorMessage, passkeysSupported, signInWithPasskey } from './-auth/passkeys'

export const Route = createFileRoute('/sign-in')({
  // `redirect` is kept only as a path on this origin (spec 0005, AC-20).
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => {
    const redirect = safeRedirect(search.redirect)
    return redirect === undefined ? {} : { redirect }
  },
  component: SignIn,
})

/**
 * Sign in with an email and password (spec 0004, AC-27), or a passkey from the button or the email
 * field's autofill (spec 0013, AC-41). An account with MFA then passes the Two step verification
 * step. Orvano sets the session cookies; the page then clears cached data and goes back to where
 * you were headed. While the install waits for its first admin, the form gives way to the setup
 * notice (spec 0006, AC-23).
 */
function SignIn() {
  usePageTitle('Sign in')
  const { redirect } = Route.useSearch()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const setup = useQuery(setupQuery())
  const [mfa, setMfa] = useState<MfaChallenge | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [passkeyError, setPasskeyError] = useState<string | null>(null)
  const [passkeys, setPasskeys] = useState(false)
  const [passkeyBusy, setPasskeyBusy] = useState(false)
  const autofill = useRef<AbortController | null>(null)
  const showForm = setup.data?.setupRequired !== true && mfa === null

  const signedIn = async () => {
    queryClient.clear()
    await navigate({ href: redirect ?? '/', replace: true })
  }

  // Offer passkeys in the email field's autofill while the form shows. Only Orvano refusing a
  // passkey you picked (a blocked account, too many attempts, a passkey that didn't work) shows, like
  // the button's; anything else just ends it (see `autofillEnded`).
  useEffect(() => {
    if (!showForm) return
    const controller = new AbortController()
    autofill.current = controller
    void passkeysSupported().then(setPasskeys)
    void passkeysSupported(true).then(async (supported) => {
      if (!supported || controller.signal.aborted) return
      try {
        await signInWithPasskey({ autofill: true, signal: controller.signal })
      } catch (error) {
        if (!autofillEnded(error, controller.signal)) setPasskeyError(passkeyErrorMessage(error))
        return
      }
      queryClient.clear()
      await navigate({ href: redirect ?? '/', replace: true })
    })
    return () => {
      controller.abort()
    }
  }, [showForm, navigate, queryClient, redirect])

  const content = () => {
    if (setup.data?.setupRequired === true) {
      return (
        <FormAlert variant="info" title="Finish setting up Orvano">
          Open the setup link the installer printed on your server.
        </FormAlert>
      )
    }
    if (mfa !== null) {
      return (
        <MfaStep
          factors={mfa.factors}
          onSignedIn={signedIn}
          onStartOver={(reason) => {
            setNotice(reason ?? null)
            setMfa(null)
          }}
        />
      )
    }
    return (
      <>
        {notice === null ? null : (
          <FormAlert variant="warning" title="Sign in again">
            {notice}
          </FormAlert>
        )}
        {passkeyError === null ? null : (
          <FormAlert title="Couldn't sign in with a passkey">{passkeyError}</FormAlert>
        )}
        <SignInForm
          passkeyAutofill
          onSubmit={async (values) => {
            const result = await consoleApi().consoleAccount.createSession(values)
            setNotice(null)
            setPasskeyError(null)
            if (result.mfa !== null) {
              setMfa(result.mfa)
              return
            }
            await signedIn()
          }}
        />
        {passkeys ? (
          <Button
            variant="outline"
            loading={passkeyBusy}
            onClick={() => {
              autofill.current?.abort()
              setPasskeyBusy(true)
              setNotice(null)
              setPasskeyError(null)
              signInWithPasskey()
                .then(signedIn)
                .catch((error: unknown) => {
                  setPasskeyError(passkeyErrorMessage(error))
                })
                .finally(() => {
                  setPasskeyBusy(false)
                })
            }}
          >
            Sign in with a passkey
          </Button>
        ) : null}
        {/* Only on an install with open sign up (spec 0008, AC-23). */}
        {setup.data?.signupOpen === true ? (
          <p className="text-muted-foreground">
            New here?{' '}
            <Link to="/sign-up" className="text-link underline">
              Create account
            </Link>
          </p>
        ) : null}
      </>
    )
  }

  return (
    <main id="main" className="mx-auto flex max-w-md flex-col gap-6 px-(--page-px) py-24">
      <LogoMark className="size-10 text-primary" />
      <PageHeading>Sign in</PageHeading>
      {content()}
    </main>
  )
}

/**
 * Whether an autofill passkey attempt ended quietly. Autofill runs in the background, so only
 * Orvano refusing a passkey you picked is worth showing. Everything else ends it without a word: you
 * left the page, the prompt closed, the browser can't offer passkeys here (a browser with no
 * authenticator rejects with `NotSupportedError`), the network failed, or this server has no console
 * passkeys. The button still reports every failure.
 */
function autofillEnded(error: unknown, signal: AbortSignal): boolean {
  return signal.aborted || !(error instanceof OrvanoError) || error.code === 'factor_not_enabled'
}
