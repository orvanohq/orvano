import type { ReactNode } from 'react'

/**
 * The only way a page renders its `h1`: the root moves focus to `#page-title` after every in app
 * navigation so screen readers announce the new page (spec 0005, AC-23).
 */
export function PageHeading({ children }: { children: ReactNode }) {
  return (
    <h1 id="page-title" tabIndex={-1} className="text-2xl/8 font-semibold outline-none">
      {children}
    </h1>
  )
}
