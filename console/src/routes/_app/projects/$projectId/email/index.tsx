import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_app/projects/$projectId/email/')({
  // The Email entry opens its first tab (spec 0009, Screens).
  beforeLoad: ({ params }) => {
    throw redirect({ to: '/projects/$projectId/email/settings', params, replace: true })
  },
})
