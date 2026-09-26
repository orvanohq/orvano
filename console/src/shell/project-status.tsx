import { CircleAlert, Loader2, Trash2 } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'

import { usePageTitle } from '@/lib/page-title'
import { formatFull } from '@/lib/format'
import { PageHeading } from '@/shell/page-heading'
import type { Project } from '@orvano/console-client'

/** How long "Setting up" runs before it adds the "taking longer than usual" note. */
const slowAfterMs = 60_000

/**
 * What a project page shows instead of its product pages while the project is not `active` (AC-18).
 * Each panel has an action slot that row 7 fills (retry, restore, retry purge).
 */
export function ProjectStatusPanel({ project }: { project: Project }) {
  switch (project.status) {
    case 'provisioning':
      return <SettingUp project={project} />
    case 'failed':
      return (
        <Panel
          icon={<CircleAlert aria-hidden className="text-danger-text" />}
          project={project}
          title="Setup failed"
        >
          <p>Something went wrong while setting this project up.</p>
        </Panel>
      )
    case 'deleting':
      return (
        <Panel icon={<Trash2 aria-hidden />} project={project} title="Being deleted">
          <p>
            This project is being deleted
            {project.purgeAfter === null
              ? '.'
              : ` and is purged for good on ${formatFull(project.purgeAfter)}.`}
          </p>
          {project.purgeFailedAt === null ? null : (
            <p className="text-danger-text">
              The last purge attempt failed on {formatFull(project.purgeFailedAt)}.
            </p>
          )}
        </Panel>
      )
    case 'active':
      return null
  }
}

function SettingUp({ project }: { project: Project }) {
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSlow(true)
    }, slowAfterMs)
    return () => {
      window.clearTimeout(timer)
    }
  }, [])
  return (
    <Panel
      icon={<Loader2 aria-hidden className="animate-spin" />}
      project={project}
      title="Setting up"
      live
    >
      <p>Your project is being set up. This page updates when it is ready.</p>
      {slow ? <p className="text-warning-text">This is taking longer than usual.</p> : null}
    </Panel>
  )
}

function Panel({
  icon,
  title,
  project,
  live = false,
  children,
}: {
  icon: ReactNode
  title: string
  project: Project
  live?: boolean
  children: ReactNode
}) {
  usePageTitle(title, project.name)
  return (
    <div className="mx-auto flex max-w-xl flex-col gap-3 rounded-lg border border-border bg-card p-6">
      <div className="flex items-center gap-2 text-muted-foreground">
        {icon}
        <span>{project.name}</span>
      </div>
      <PageHeading>{title}</PageHeading>
      <div role={live ? 'status' : undefined} className="flex flex-col gap-2 text-body">
        {children}
      </div>
      {/* Row 7 fills this action slot. */}
      <div data-slot="status-action" />
    </div>
  )
}
