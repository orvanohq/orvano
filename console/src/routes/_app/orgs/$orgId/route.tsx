import { useQuery } from '@tanstack/react-query'
import { Outlet, createFileRoute, notFound } from '@tanstack/react-router'
import { Trash2 } from 'lucide-react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { isNotFound } from '@/lib/errors'
import { formatFull } from '@/lib/format'
import { orgQuery } from '@/lib/queries'
import { InShellNotFound } from '@/shell/in-shell-not-found'

import { RestoreOrgButton } from './-org-settings/org-actions'

export const Route = createFileRoute('/_app/orgs/$orgId')({
  loader: async ({ context, params }) => {
    try {
      await context.queryClient.query({ ...orgQuery(params.orgId), staleTime: 'static' })
    } catch (error) {
      if (isNotFound(error, 'not_found')) throw notFound()
      throw error
    }
  },
  notFoundComponent: () => <InShellNotFound what="org" />,
  component: OrgLayout,
})

function OrgLayout() {
  const { orgId } = Route.useParams()
  const org = useQuery(orgQuery(orgId)).data
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      {org?.status === 'deleting' ? (
        <Alert role="status">
          <Trash2 aria-hidden />
          <AlertTitle>Deleting</AlertTitle>
          <AlertDescription>
            This org is being deleted
            {org.purgeAfter === null
              ? ''
              : ` and is purged for good on ${formatFull(org.purgeAfter)}`}
            . Restoring it restores none of its projects.
          </AlertDescription>
          <div className="col-start-2 mt-2">
            <RestoreOrgButton org={org} variant="outline" />
          </div>
        </Alert>
      ) : null}
      <Outlet />
    </div>
  )
}
