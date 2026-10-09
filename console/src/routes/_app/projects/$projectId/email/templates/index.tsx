import { useQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { usePageTitle } from '@/lib/page-title'
import { emailTemplatesQuery, projectQuery } from '@/lib/queries'
import { ErrorPanel } from '@/shell/error-panel'
import { RelativeTime } from '@/shell/relative-time'

export const Route = createFileRoute('/_app/projects/$projectId/email/templates/')({
  component: EmailTemplatesPage,
})

/**
 * The Email Templates tab (spec 0009, AC-8; spec 0013, AC-31): the five auth emails, each with what it is for, whether
 * the project edited it, and when. Each row opens its editor.
 */
function EmailTemplatesPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Email templates', project?.name)
  const catalog = useQuery(emailTemplatesQuery(projectId))

  if (catalog.isError) {
    return (
      <ErrorPanel
        error={catalog.error}
        onRetry={() => {
          void catalog.refetch()
        }}
      />
    )
  }

  return (
    <section aria-labelledby="templates-heading" className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 id="templates-heading" className="text-lg font-semibold">
          Templates
        </h2>
        <p className="max-w-prose text-muted-foreground">
          The emails Orvano sends to your users. Each starts from a default you can rewrite in your
          product’s own words.
        </p>
      </div>
      {catalog.data === undefined ? (
        <div aria-busy className="flex flex-col gap-2">
          {[0, 1, 2, 3].map((row) => (
            <Skeleton key={row} aria-hidden className="h-16 w-full" />
          ))}
        </div>
      ) : (
        <ul className="divide-y divide-border rounded-lg border border-border bg-card">
          {catalog.data.templates.map((template) => (
            <li
              key={template.kind}
              className="relative flex items-center gap-3 px-4 py-3 hover:bg-accent"
            >
              <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <Link
                  to="/projects/$projectId/email/templates/$kind"
                  params={{ projectId, kind: template.kind }}
                  // The whole row is the link's target.
                  className="w-fit font-medium after:absolute after:inset-0"
                >
                  {template.name}
                </Link>
                <span className="text-muted-foreground">{template.description}</span>
              </div>
              {template.updatedAt === null ? null : (
                <span className="hidden text-small whitespace-nowrap text-muted-foreground sm:inline">
                  Edited <RelativeTime iso={template.updatedAt} />
                </span>
              )}
              <Badge variant={template.isCustom ? 'primary' : 'neutral'}>
                {template.isCustom ? 'Custom' : 'Default'}
              </Badge>
              <ChevronRight aria-hidden className="size-(--icon) shrink-0 text-muted-foreground" />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
