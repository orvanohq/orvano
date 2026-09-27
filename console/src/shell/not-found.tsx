import { Link } from '@tanstack/react-router'

import { buttonVariants } from '@/components/ui/button'
import { usePageTitle } from '@/lib/page-title'
import { PageHeading } from '@/shell/page-heading'

/** The 404 page for any URL that matches no route. */
export function NotFoundPage() {
  usePageTitle('Page not found')
  return (
    <main id="main" className="mx-auto max-w-xl px-(--page-px) py-16">
      <PageHeading>Page not found</PageHeading>
      <p className="mt-2 text-muted-foreground">
        This page doesn&apos;t exist. Check the address or go back to your orgs.
      </p>
      <Link to="/orgs" className={`${buttonVariants({ variant: 'outline' })} mt-6`}>
        Go to orgs
      </Link>
    </main>
  )
}
