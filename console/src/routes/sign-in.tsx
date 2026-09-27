import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'

import { FormAlert } from '@/components/ui/form-alert'
import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { setupQuery } from '@/lib/queries'
import { safeRedirect } from '@/lib/redirect'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'

import { SignInForm } from './-auth/auth-form'

export const Route = createFileRoute('/sign-in')({
  // `redirect` is kept only as a path on this origin (spec 0005, AC-20).
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => {
    const redirect = safeRedirect(search.redirect)
    return redirect === undefined ? {} : { redirect }
  },
  component: SignIn,
})

/**
 * Sign in with an email and password (spec 0004, AC-27). Orvano sets the session cookies; the page
 * then clears cached data and goes back to where you were headed. While the install waits for its
 * first admin, the form gives way to the setup notice (spec 0006, AC-23).
 */
function SignIn() {
  usePageTitle('Sign in')
  const { redirect } = Route.useSearch()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const setup = useQuery(setupQuery())

  return (
    <main id="main" className="mx-auto flex max-w-md flex-col gap-6 px-(--page-px) py-24">
      <LogoMark className="size-10 text-primary" />
      <PageHeading>Sign in</PageHeading>
      {setup.data?.setupRequired === true ? (
        <FormAlert variant="info" title="Finish setting up Orvano">
          Open the setup link the installer printed on your server.
        </FormAlert>
      ) : (
        <SignInForm
          onSubmit={async (values) => {
            await consoleApi().consoleAccount.createSession(values)
            queryClient.clear()
            await navigate({ href: redirect ?? '/', replace: true })
          }}
        />
      )}
    </main>
  )
}
