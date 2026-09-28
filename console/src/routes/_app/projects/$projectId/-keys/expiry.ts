/** The Expiry choices of the Create key form (spec 0007, AC-13). */
export type Expiry = 'never' | '30' | '90' | '365' | 'custom'

/** The Expiry select's items, in order, Never first (the default). */
export const expiryOptions: readonly { value: Expiry; label: string }[] = [
  { value: 'never', label: 'Never' },
  { value: '30', label: '30 days' },
  { value: '90', label: '90 days' },
  { value: '365', label: '365 days' },
  { value: 'custom', label: 'Custom date' },
]

const dayMs = 86_400_000

/** Today in the browser's time zone as `YYYY-MM-DD`: the custom date field's earliest choice. */
export function todayLocal(now: number = Date.now()): string {
  const date = new Date(now)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${String(date.getFullYear())}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/**
 * The `expiresAt` to send, as ISO 8601 UTC, or undefined for Never (spec 0007, Value sourcing).
 * N days is `now` plus N × 24 hours; a custom date is the end of that day (23:59:59.999) in the
 * browser's time zone.
 */
export function expiresAtFor(
  expiry: Expiry,
  customDate: string,
  now: number = Date.now(),
): string | undefined {
  switch (expiry) {
    case 'never':
      return undefined
    case '30':
    case '90':
    case '365':
      return new Date(now + Number(expiry) * dayMs).toISOString()
    case 'custom': {
      const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(customDate)
      if (match === null) return undefined
      const [, year, month, day] = match.map(Number)
      return new Date(year, month - 1, day, 23, 59, 59, 999).toISOString()
    }
  }
}

/** True when a key's `expiresAt` is at or before now, so the table shows "Expired". */
export function isExpired(expiresAt: string | null, now: number = Date.now()): boolean {
  return expiresAt !== null && new Date(expiresAt).getTime() <= now
}
