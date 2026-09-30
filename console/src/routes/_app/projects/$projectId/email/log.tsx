import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

import { EmailLogTable } from '@/email/email-log-table'
import { usePageTitle } from '@/lib/page-title'
import { emailsQuery, projectQuery } from '@/lib/queries'

export const Route = createFileRoute('/_app/projects/$projectId/email/log')({
  component: EmailLogPage,
})

/**
 * The Email Log tab (spec 0009, AC-20): the project's emails of the last 30 days with their status,
 * for every member. Recipients are masked, and no content is kept.
 */
function EmailLogPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Email log', project?.name)
  const emails = useInfiniteQuery(emailsQuery(projectId))
  return <EmailLogTable label="Email log" emails={emails} />
}
