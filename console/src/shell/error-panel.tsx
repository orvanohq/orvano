import { TriangleAlert } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { describeError } from '@/lib/errors'

/** A page's load failure: the message, code, and request ID, with a Retry that loads it again (AC-21). */
export function ErrorPanel({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const { message, code, requestId } = describeError(error)
  return (
    <div role="alert" className="mx-auto max-w-xl rounded-lg border border-border bg-card p-6">
      <div className="flex items-center gap-2 text-h3 font-semibold text-danger-text">
        <TriangleAlert aria-hidden className="size-(--icon)" />
        This page didn&apos;t load
      </div>
      <p className="mt-2 text-body">{message}</p>
      {code !== null || requestId !== null ? (
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-small text-muted-foreground">
          {code !== null ? (
            <>
              <dt>Code</dt>
              <dd className="font-mono">{code}</dd>
            </>
          ) : null}
          {requestId !== null ? (
            <>
              <dt>Request ID</dt>
              <dd className="font-mono">{requestId}</dd>
            </>
          ) : null}
        </dl>
      ) : null}
      <Button className="mt-4" variant="outline" onClick={onRetry}>
        Retry
      </Button>
    </div>
  )
}
