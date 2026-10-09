import { useQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'
import { MailQuestion } from 'lucide-react'

import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { templateLabels } from '@/email/email-log'
import { isNotFound } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import { emailTemplateQuery, emailTemplatesQuery, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { ErrorPanel } from '@/shell/error-panel'
import { meetsRole } from '@/shell/nav'

import { TemplateEditor } from '../../-email/template-editor'

export const Route = createFileRoute('/_app/projects/$projectId/email/templates/$kind')({
  component: EmailTemplatePage,
})

/**
 * One email template (spec 0009, AC-9): its editor for owners and developers, the same page read
 * only for a viewer. A kind that is not one of the five says so, inside the Email area.
 */
function EmailTemplatePage() {
  const { projectId, kind } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  const role = useOrgRole()
  const template = useQuery({
    ...emailTemplateQuery(projectId, kind),
    // The editor keeps its own copy while you type; a background refetch must not replace it.
    refetchOnWindowFocus: false,
    retry: (count, error) => !isNotFound(error, 'not_found') && count < 3,
  })
  const summary = useQuery(emailTemplatesQuery(projectId)).data?.templates.find(
    (item) => item.kind === kind,
  )
  const name =
    summary?.name ?? (template.data === undefined ? 'Template' : templateLabels[template.data.kind])
  usePageTitle(`${name} template`, project?.name)

  const back = (
    <Link
      to="/projects/$projectId/email/templates"
      params={{ projectId }}
      className="w-fit text-link hover:underline"
    >
      All templates
    </Link>
  )

  if (template.isError) {
    if (isNotFound(template.error, 'not_found')) {
      return (
        <div className="flex flex-col gap-4">
          {back}
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <MailQuestion aria-hidden />
              </EmptyMedia>
              <EmptyTitle>No such template</EmptyTitle>
              <EmptyDescription>
                There are five: email verification, password reset, magic link, email code, and
                security alert.
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        </div>
      )
    }
    return (
      <ErrorPanel
        error={template.error}
        onRetry={() => {
          void template.refetch()
        }}
      />
    )
  }

  return (
    <div className="flex flex-col gap-4">
      {back}
      {template.data === undefined || role === undefined ? (
        <div aria-busy className="flex flex-col gap-4">
          <Skeleton aria-hidden className="h-12 w-72" />
          <Skeleton aria-hidden className="h-96 w-full" />
        </div>
      ) : (
        <TemplateEditor
          // A fresh editor for each template, never one that carries another's text.
          key={`${projectId}:${kind}`}
          projectId={projectId}
          orgId={project?.orgId}
          template={template.data}
          name={name}
          description={summary?.description}
          readOnlyReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
        />
      )}
    </div>
  )
}
