import { useRouter, type ErrorComponentProps } from '@tanstack/react-router'

import { isSessionError } from '@/lib/session'
import { ErrorPanel } from '@/shell/error-panel'

/**
 * The error panel for a route whose data failed to load (5xx, network, or timeout). A session error
 * renders nothing: the redirect to sign in is already on its way.
 */
export function RouteError({ error, reset }: ErrorComponentProps) {
  const router = useRouter()
  if (isSessionError(error)) return null
  return (
    <ErrorPanel
      error={error}
      onRetry={() => {
        reset()
        void router.invalidate()
      }}
    />
  )
}
