import { describe, expect, it } from 'vitest'

import { expiresAtFor, isExpired, todayLocal } from './expiry.ts'

// The browser's time zone decides the custom date's end of day (spec 0007, critical test scenarios).
process.env.TZ = 'America/Los_Angeles'

describe('expiresAtFor (AC-13)', () => {
  const now = Date.parse('2026-10-01T12:00:00Z')

  it('sends nothing for Never', () => {
    expect(expiresAtFor('never', '', now)).toBeUndefined()
  })

  it('adds whole days of 24 hours to now', () => {
    expect(expiresAtFor('30', '', now)).toBe('2026-10-31T12:00:00.000Z')
    expect(expiresAtFor('365', '', now)).toBe('2027-10-01T12:00:00.000Z')
  })

  it('ends a custom date at 23:59:59.999 in the browser time zone', () => {
    expect(expiresAtFor('custom', '2026-10-05', now)).toBe('2026-10-06T06:59:59.999Z')
  })
})

describe('todayLocal', () => {
  it('is the local calendar date, not the UTC one', () => {
    // 03:00 UTC on 2 October is still 1 October in Los Angeles.
    expect(todayLocal(Date.parse('2026-10-02T03:00:00Z'))).toBe('2026-10-01')
  })
})

describe('isExpired (AC-12)', () => {
  const now = Date.parse('2026-10-01T12:00:00Z')

  it('is false for a key that never expires or expires later', () => {
    expect(isExpired(null, now)).toBe(false)
    expect(isExpired('2026-10-01T12:00:01Z', now)).toBe(false)
  })

  it('is true at or before now', () => {
    expect(isExpired('2026-10-01T12:00:00Z', now)).toBe(true)
    expect(isExpired('2026-09-01T00:00:00Z', now)).toBe(true)
  })
})
