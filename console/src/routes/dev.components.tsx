import { createFileRoute, notFound } from '@tanstack/react-router'
import { lazy, type ComponentType } from 'react'

// Dev only (spec 0005, AC-9). The catalog is imported lazily inside an `import.meta.env.DEV`
// branch and nothing else imports `src/dev/`, so the production build drops it and this route
// answers with the 404 page.
let Catalog: ComponentType | undefined
if (import.meta.env.DEV) {
  Catalog = lazy(() => import('@/dev/catalog').then((module) => ({ default: module.Catalog })))
}

export const Route = createFileRoute('/dev/components')({
  beforeLoad: () => {
    if (!import.meta.env.DEV) throw notFound()
  },
  component: () => (Catalog === undefined ? null : <Catalog />),
})
