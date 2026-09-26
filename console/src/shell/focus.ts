import type { AnyRouter } from '@tanstack/react-router'

/**
 * After every in app navigation (not the first load) moves focus to `#page-title`, so a screen
 * reader announces the new page (spec 0005, AC-23).
 */
export function focusPageTitleOnNavigate(router: AnyRouter): void {
  let firstLoad = true
  router.subscribe('onResolved', (event) => {
    // The first resolve is the page load itself, redirects included; focus stays where the browser put it.
    if (firstLoad) {
      firstLoad = false
      return
    }
    if (!event.pathChanged) return
    // Let the new page commit before looking for its heading.
    window.setTimeout(() => {
      document.getElementById('page-title')?.focus({ preventScroll: true })
    }, 0)
  })
}
