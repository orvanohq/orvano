import { createFileRoute } from '@tanstack/react-router'

import { accountQuery } from '@/lib/queries'
import { AppLayout } from '@/shell/app-layout'

// The session guard: the signed in console account is the probe, and the account menu needs it
// anyway. A 401 lands in the query cache's onError, which redirects to /sign-in once (spec 0005,
// AC-20).
export const Route = createFileRoute('/_app')({
  beforeLoad: async ({ context }) => {
    await context.queryClient.query({ ...accountQuery(), staleTime: 'static' })
  },
  component: AppLayout,
})
