import { createFileRoute } from '@tanstack/react-router'

import { orgsQuery } from '@/lib/queries'
import { AppLayout } from '@/shell/app-layout'

// The session guard: the switcher needs the first page of orgs anyway, so it doubles as the probe.
// A 401 lands in the query cache's onError, which redirects to /sign-in once (spec 0005, AC-20).
export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context }) => {
    await context.queryClient.infiniteQuery({ ...orgsQuery(), staleTime: 'static' })
  },
  component: AppLayout,
})
