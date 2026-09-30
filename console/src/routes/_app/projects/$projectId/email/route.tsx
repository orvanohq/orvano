import { Link, Outlet, createFileRoute } from '@tanstack/react-router'

import { PageHeading } from '@/shell/page-heading'

export const Route = createFileRoute('/_app/projects/$projectId/email')({
  component: EmailLayout,
})

/** The Email tabs, each its own route so it deep links. Later slices of spec 0009 add Templates and Log. */
const tabs = [{ label: 'Settings', to: '/projects/$projectId/email/settings' }] as const

/** The project's Email area (spec 0009, Screens): the heading, the tabs, and the tab's page below. */
function EmailLayout() {
  const { projectId } = Route.useParams()
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading>Email</PageHeading>
        <p className="max-w-prose text-muted-foreground">
          The email this project sends to its users: sign in links, codes, and password resets.
        </p>
      </div>
      <nav aria-label="Email sections" className="border-b border-border">
        <ul className="-mb-px flex gap-1">
          {tabs.map((tab) => (
            <li key={tab.to}>
              <Link
                to={tab.to}
                params={{ projectId }}
                className="flex h-(--control-h) items-center border-b-2 border-transparent px-2.5 text-body font-medium text-muted-foreground hover:text-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground"
                activeProps={{ 'aria-current': 'page' }}
              >
                {tab.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
      <Outlet />
    </div>
  )
}
