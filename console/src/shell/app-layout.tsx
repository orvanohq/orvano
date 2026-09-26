import { useQuery } from '@tanstack/react-query'
import { Outlet, useParams } from '@tanstack/react-router'
import { Menu } from 'lucide-react'
import { useRef } from 'react'

import { Sidebar, SidebarProvider, SidebarTrigger, useSidebar } from '@/components/ui/sidebar'
import { TooltipProvider } from '@/components/ui/tooltip'
import { orgQuery, projectQuery } from '@/lib/queries'
import { orgNav, projectNav } from '@/shell/nav'
import { OrgSwitcher } from '@/shell/org-switcher'
import { ProjectSwitcher } from '@/shell/project-switcher'
import { SidebarNav } from '@/shell/sidebar-nav'
import { TopBar } from '@/shell/top-bar'

/**
 * The frame every signed in page lives in: skip link, top bar with the switchers, the sidebar in
 * org and project context, and the one `main` landmark (AC-10).
 */
export function AppLayout() {
  return (
    <TooltipProvider>
      <SidebarProvider>
        <Frame />
      </SidebarProvider>
    </TooltipProvider>
  )
}

function Frame() {
  const { orgId: orgParam, projectId } = useParams({ strict: false })
  const mainRef = useRef<HTMLElement | null>(null)
  const { isDesktop } = useSidebar()

  const project = useQuery({ ...projectQuery(projectId ?? ''), enabled: projectId !== undefined })
  const orgId = orgParam ?? project.data?.orgId
  const org = useQuery({ ...orgQuery(orgId ?? ''), enabled: orgId !== undefined })

  const inProject = projectId !== undefined
  const inOrg = !inProject && orgParam !== undefined
  const hasSidebar = inProject || inOrg
  const role = org.data?.role
  const active = project.data?.status === 'active'

  const currentOrg = org.data
  const orgSwitcher = <OrgSwitcher current={currentOrg} />

  return (
    <div className="flex min-h-svh flex-col">
      <a
        href="#main"
        className="skip-link"
        onClick={(event) => {
          event.preventDefault()
          mainRef.current?.focus()
        }}
      >
        Skip to content
      </a>
      <TopBar
        leading={
          hasSidebar ? (
            <SidebarTrigger>
              <Menu aria-hidden />
            </SidebarTrigger>
          ) : undefined
        }
      >
        {/* Below 640 px only the deepest switcher stays in the bar (AC-17). */}
        <div className={inProject ? 'hidden sm:block' : undefined}>{orgSwitcher}</div>
        {inProject && orgId !== undefined ? (
          <>
            <span aria-hidden className="hidden text-muted-foreground sm:inline">
              /
            </span>
            <ProjectSwitcher orgId={orgId} current={project.data} />
          </>
        ) : null}
      </TopBar>
      <div className="flex flex-1">
        {hasSidebar ? (
          <Sidebar label={inProject ? 'Project' : 'Org'}>
            {!isDesktop && inProject ? (
              <div className="border-b border-sidebar-border p-2 sm:hidden">{orgSwitcher}</div>
            ) : null}
            <SidebarNav
              label={inProject ? 'Project navigation' : 'Org navigation'}
              entries={
                inProject ? projectNav.filter((entry) => entry.id === 'overview' || active) : orgNav
              }
              params={{ orgId: orgParam, projectId }}
              role={role}
            />
          </Sidebar>
        ) : null}
        <main
          id="main"
          ref={mainRef}
          tabIndex={-1}
          className="min-w-0 flex-1 px-(--page-px) py-6 outline-none"
        >
          <Outlet />
        </main>
      </div>
    </div>
  )
}
