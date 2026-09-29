import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, Navigate, createFileRoute, useNavigate } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'
import { useState } from 'react'

import { FormAlert } from '@/components/ui/form-alert'
import { Skeleton } from '@/components/ui/skeleton'
import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { optionalAccountQuery, setupQuery } from '@/lib/queries'
import { ErrorPanel } from '@/shell/error-panel'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'

import { SignUpForm } from './-auth/auth-form'

export const Route = createFileRoute('/sign-up')({
  component: SignUp,
})

/** What an install with invite only sign up says (spec 0008, AC-23). */
function InviteOnly() {
  return (
    <FormAlert variant="info" title="Sign up is by invitation">
      Sign up on this server is by invitation. Ask an org owner for an invite link.{' '}
      <Link to="/sign-in" className="text-link underline">
        Sign in
      </Link>
    </FormAlert>
  )
}

/**
 * Open sign up (spec 0008, AC-23). While the install waits for its first admin it sends you to
 * `/setup`, and when you are already signed in to the console. On an invite only install it says so
 * and links to sign in; otherwise it shows the sign up form and lands you in the shell like a sign
 * in does. A `signup_closed` answer (the mode changed after the page loaded) shows the invite only
 * message.
 */
function SignUp() {
  usePageTitle('Sign up')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const setup = useQuery(setupQuery())
  const account = useQuery(optionalAccountQuery())
  const [closed, setClosed] = useState(false)

  if (setup.data?.setupRequired === true) return <Navigate to="/setup" replace />
  if (account.data != null) return <Navigate to="/" replace />

  return (
    <main id="main" className="mx-auto flex max-w-md flex-col gap-6 px-(--page-px) py-24">
      <LogoMark className="size-10 text-primary" />
      <PageHeading>Create your account</PageHeading>
      {setup.isError ? (
        <ErrorPanel
          error={setup.error}
          onRetry={() => {
            void setup.refetch()
          }}
        />
      ) : setup.isPending || account.isPending ? (
        <Skeleton aria-hidden className="h-64 w-full" />
      ) : !setup.data.signupOpen || closed ? (
        <InviteOnly />
      ) : (
        <>
          <SignUpForm
            onSubmit={async ({ name, email, password }) => {
              try {
                await consoleApi().consoleAccount.create({
                  email,
                  password,
                  name: name === '' ? null : name,
                })
              } catch (error) {
                if (error instanceof OrvanoError && error.code === 'signup_closed') setClosed(true)
                throw error
              }
              queryClient.clear()
              await navigate({ to: '/', replace: true })
            }}
          />
          <p className="text-muted-foreground">
            Already have an account?{' '}
            <Link to="/sign-in" className="text-link underline">
              Sign in
            </Link>
          </p>
        </>
      )}
    </main>
  )
}
