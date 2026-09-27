import { Link } from '@tanstack/react-router'

import { buttonVariants } from '@/components/ui/button'
import { usePageTitle } from '@/lib/page-title'
import { PageHeading } from '@/shell/page-heading'

/** "Doesn't exist or you don't have access", shown inside the shell so the top bar keeps working (AC-19). */
export function InShellNotFound({ what }: { what: 'project' | 'org' | 'user' }) {
  const noun = { org: 'Org', project: 'Project', user: 'User' }[what]
  usePageTitle(`${noun} not found`)
  return (
    <div className="mx-auto max-w-xl py-10">
      <PageHeading>{noun} not found</PageHeading>
      <p className="mt-2 text-muted-foreground">
        This {what} doesn&apos;t exist or you don&apos;t have access to it.
      </p>
      <Link to="/orgs" className={`${buttonVariants({ variant: 'outline' })} mt-6`}>
        Go to orgs
      </Link>
    </div>
  )
}
