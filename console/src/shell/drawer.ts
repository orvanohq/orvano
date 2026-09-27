import { useRouterState } from '@tanstack/react-router'
import { useEffect } from 'react'

import { useSidebar } from '@/components/ui/sidebar'

/**
 * Shuts the mobile sidebar drawer whenever the location changes, however it changed: a link in the
 * drawer, a switcher inside it, or the back button (spec 0005, AC-16). Without it the drawer stays
 * open over the new page and `focusPageTitleOnNavigate` moves focus behind it.
 */
export function useCloseDrawerOnNavigate(): void {
  const { setDrawerOpen } = useSidebar()
  const href = useRouterState({ select: (state) => state.location.href })
  useEffect(() => {
    setDrawerOpen(false)
  }, [href, setDrawerOpen])
}
