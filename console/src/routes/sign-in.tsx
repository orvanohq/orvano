import { createFileRoute } from '@tanstack/react-router'

import { safeRedirect } from '@/lib/redirect'
import { LogoMark } from '@/shell/logo'
import { PageHeading } from '@/shell/page-heading'

export const Route = createFileRoute('/sign-in')({
  // `redirect` is kept only as a path on this origin (spec 0005, AC-20).
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => {
    const redirect = safeRedirect(search.redirect)
    return redirect === undefined ? {} : { redirect }
  },
  head: () => ({ meta: [{ title: 'Sign in · Orvano' }] }),
  component: SignIn,
})

// Placeholder until row 8 builds the real sign in.
function SignIn() {
  return (
    <main id="main" className="mx-auto max-w-md px-(--page-px) py-24">
      <LogoMark className="mb-6 size-10 text-primary" />
      <PageHeading>Sign in</PageHeading>
      <p className="mt-2 text-muted-foreground">
        Sign in arrives with the accounts row. Until then the console needs a session cookie from
        the server.
      </p>
    </main>
  )
}
