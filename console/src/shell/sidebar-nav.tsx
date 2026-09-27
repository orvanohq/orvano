import { Link } from '@tanstack/react-router'

import { SidebarToggle, useSidebar } from '@/components/ui/sidebar'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { meetsRole, type NavEntry } from '@/shell/nav'
import { cn } from '@/lib/utils'
import type { OrgRole } from '@orvano/console-client'

interface SidebarNavProps {
  /** The navigation landmark's name, for example "Project navigation". */
  label: string
  entries: readonly NavEntry[]
  params: { orgId?: string; projectId?: string }
  role: OrgRole | undefined
}

/** The sidebar's links, filtered by role. The current entry carries `aria-current="page"`. */
export function SidebarNav({ label, entries, params, role }: SidebarNavProps) {
  const { collapsed, isDesktop, setDrawerOpen } = useSidebar()
  const rail = collapsed && isDesktop
  return (
    <>
      <nav aria-label={label} className="flex-1 overflow-y-auto p-2">
        <ul className="flex flex-col gap-0.5">
          {entries
            .filter((entry) => meetsRole(role, entry.minRole))
            .map((entry) => {
              const Icon = entry.icon
              const link = (
                <Link
                  to={entry.to}
                  // Entries name their own `to`, so TypeScript cannot pair it with these params.
                  params={params as never}
                  activeOptions={{ exact: entry.exact ?? false }}
                  className={cn(
                    'flex h-(--control-h) items-center gap-2 rounded-md px-2.5 text-body font-medium text-sidebar-foreground hover:bg-sidebar-accent [&.active]:bg-sidebar-accent',
                    rail && 'justify-center px-0',
                  )}
                  activeProps={{ 'aria-current': 'page' }}
                  // The location does not change when you pick the page you are on, so close here too.
                  onClick={() => {
                    setDrawerOpen(false)
                  }}
                >
                  <Icon aria-hidden className="size-(--icon) shrink-0" />
                  <span className={rail ? 'sr-only' : undefined}>{entry.label}</span>
                </Link>
              )
              return (
                <li key={entry.id}>
                  {rail ? (
                    <Tooltip>
                      <TooltipTrigger render={link} />
                      <TooltipContent side="right">{entry.label}</TooltipContent>
                    </Tooltip>
                  ) : (
                    link
                  )}
                </li>
              )
            })}
        </ul>
      </nav>
      <SidebarToggle />
    </>
  )
}
