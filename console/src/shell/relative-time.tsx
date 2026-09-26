import { formatFull, formatRelative } from '@/lib/format'

/** A time as "3 minutes ago" with the full date as its tooltip and machine readable value. */
export function RelativeTime({ iso }: { iso: string }) {
  return (
    <time dateTime={iso} title={formatFull(iso)}>
      {formatRelative(iso)}
    </time>
  )
}
