import { describe, expect, it } from 'vitest'

import { formatFull, formatRelative } from './format.ts'

// The formatter follows the browser's locale, so the expected text comes from the same platform
// formatter; what these tests pin is which unit and count `formatRelative` picks.
const reference = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
const now = Date.parse('2026-06-15T12:00:00.000Z')
const at = (secondsFromNow: number) => new Date(now + secondsFromNow * 1000).toISOString()

describe('formatRelative', () => {
  it.each([
    ['seconds', -5, reference.format(-5, 'second')],
    ['seconds, up to the last one before a minute', -59, reference.format(-59, 'second')],
    ['a minute', -60, reference.format(-1, 'minute')],
    ['minutes', -3 * 60, reference.format(-3, 'minute')],
    ['hours', -2 * 3600, reference.format(-2, 'hour')],
    ['days', -3 * 86_400, reference.format(-3, 'day')],
    ['months', -60 * 86_400, reference.format(-2, 'month')],
    ['years', -2 * 31_536_000, reference.format(-2, 'year')],
  ])('picks the unit for %s ago', (_name, secondsFromNow, expected) => {
    expect(formatRelative(at(secondsFromNow), now)).toBe(expected)
  })

  it('reads a time in the future as a time to come', () => {
    expect(formatRelative(at(3 * 60), now)).toBe(reference.format(3, 'minute'))
  })

  it('reads the current moment as now', () => {
    expect(formatRelative(at(0), now)).toBe(reference.format(0, 'second'))
  })
})

describe('formatFull', () => {
  it('includes the year', () => {
    expect(formatFull('2026-06-15T12:00:00.000Z')).toContain('2026')
  })

  it('includes the time of day, so two moments on one day read differently', () => {
    expect(formatFull('2026-06-15T12:00:00.000Z')).not.toBe(formatFull('2026-06-15T15:30:00.000Z'))
  })
})
