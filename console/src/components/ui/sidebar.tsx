import { PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ComponentProps,
  type ReactNode,
} from 'react'

import { Button } from '@/components/ui/button'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { cn } from '@/lib/utils'

/*
 * The console sidebar, trimmed from shadcn's (spec 0005): expanded 240 px, a 56 px icon rail, and a
 * drawer below 1024 px. Its state lives in localStorage (`orvano.sidebar`), never a cookie, and `[`
 * toggles it instead of Ctrl+B.
 */

const storageKey = 'orvano.sidebar'
const desktopQuery = '(min-width: 64rem)'

function readCollapsed(): boolean {
  try {
    return window.localStorage.getItem(storageKey) === 'collapsed'
  } catch {
    return false
  }
}

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)
}

interface SidebarContextValue {
  /** True when the sidebar is the 56 px rail (desktop only). */
  collapsed: boolean
  /** True at 1024 px and above; below it the sidebar is a drawer. */
  isDesktop: boolean
  drawerOpen: boolean
  setDrawerOpen: (open: boolean) => void
  toggle: () => void
}

const SidebarContext = createContext<SidebarContextValue | null>(null)

/** The sidebar's state: rail or full, drawer open or shut. */
export function useSidebar(): SidebarContextValue {
  const value = use(SidebarContext)
  if (value === null) throw new Error('useSidebar needs a SidebarProvider')
  return value
}

/** Holds the sidebar state and the `[` shortcut for everything under it. */
export function SidebarProvider({ children }: { children: ReactNode }) {
  const [collapsed, setCollapsed] = useState(readCollapsed)
  const [isDesktop, setIsDesktop] = useState(() => window.matchMedia(desktopQuery).matches)
  const [drawerOpen, setDrawerOpen] = useState(false)

  useEffect(() => {
    const media = window.matchMedia(desktopQuery)
    const onChange = (event: MediaQueryListEvent) => {
      setIsDesktop(event.matches)
      if (event.matches) setDrawerOpen(false)
    }
    media.addEventListener('change', onChange)
    return () => {
      media.removeEventListener('change', onChange)
    }
  }, [])

  const toggle = useCallback(() => {
    if (!isDesktop) {
      setDrawerOpen((open) => !open)
      return
    }
    setCollapsed((current) => {
      const next = !current
      try {
        window.localStorage.setItem(storageKey, next ? 'collapsed' : 'expanded')
      } catch {
        // Storage can be blocked; the state then lasts for this visit only.
      }
      return next
    })
  }, [isDesktop])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== '[' || event.ctrlKey || event.metaKey || event.altKey) return
      if (isTypingTarget(event.target)) return
      if (!isDesktop) return
      event.preventDefault()
      toggle()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [isDesktop, toggle])

  const value = useMemo(
    () => ({ collapsed, isDesktop, drawerOpen, setDrawerOpen, toggle }),
    [collapsed, isDesktop, drawerOpen, toggle],
  )
  return <SidebarContext value={value}>{children}</SidebarContext>
}

/** The sidebar surface: a sticky column at 1024 px and up, a left drawer below it. */
export function Sidebar({
  label,
  children,
  className,
}: {
  /** Names the drawer for assistive tech. */
  label: string
  children: ReactNode
  className?: string
}) {
  const { collapsed, isDesktop, drawerOpen, setDrawerOpen } = useSidebar()
  if (!isDesktop) {
    return (
      <Sheet open={drawerOpen} onOpenChange={setDrawerOpen}>
        <SheetContent
          side="left"
          showCloseButton={false}
          className="w-(--drawer-w) max-w-[85vw] bg-sidebar p-0 text-sidebar-foreground data-[side=left]:sm:max-w-(--drawer-w)"
        >
          <SheetHeader className="sr-only">
            <SheetTitle>{label}</SheetTitle>
            <SheetDescription>Navigation for {label}</SheetDescription>
          </SheetHeader>
          <div data-slot="sidebar" data-state="drawer" className="flex h-full flex-col">
            {children}
          </div>
        </SheetContent>
      </Sheet>
    )
  }
  return (
    <aside
      data-slot="sidebar"
      data-state={collapsed ? 'rail' : 'expanded'}
      className={cn(
        'sticky top-(--topbar-h) z-(--z-sidebar) flex h-[calc(100svh-var(--topbar-h))] shrink-0 flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground',
        collapsed ? 'w-(--sidebar-w-rail)' : 'w-(--sidebar-w)',
        className,
      )}
    >
      {children}
    </aside>
  )
}

/** The menu button in the top bar that opens the drawer; hidden at 1024 px and up. */
export function SidebarTrigger({ className, ...props }: ComponentProps<typeof Button>) {
  const { drawerOpen, setDrawerOpen } = useSidebar()
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label="Open navigation"
      aria-expanded={drawerOpen}
      className={cn('lg:hidden', className)}
      onClick={() => {
        setDrawerOpen(true)
      }}
      {...props}
    />
  )
}

/** The expand and collapse button at the foot of the desktop sidebar. */
export function SidebarToggle() {
  const { collapsed, isDesktop, toggle } = useSidebar()
  if (!isDesktop) return null
  const Icon = collapsed ? PanelLeftOpen : PanelLeftClose
  const label = collapsed ? 'Expand sidebar' : 'Collapse sidebar'
  return (
    <div className="mt-auto border-t border-sidebar-border p-2">
      <Button
        variant="ghost"
        size={collapsed ? 'icon' : 'default'}
        className="w-full justify-start"
        aria-label={label}
        aria-keyshortcuts="["
        onClick={toggle}
      >
        <Icon aria-hidden />
        {collapsed ? null : <span>Collapse</span>}
      </Button>
    </div>
  )
}
