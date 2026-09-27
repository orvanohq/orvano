import { describe, expect, it } from 'vitest'
import { render } from 'vitest-browser-react'

import { formatFull } from '@/lib/format'
import { RelativeTime } from '@/shell/relative-time'

// The wording follows the browser's locale, so the expected text comes from the same platform formatter.
const reference = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

describe('RelativeTime', () => {
  it('shows how long ago a time was, as a machine readable time element', async () => {
    const iso = new Date(Date.now() - 3 * 60_000).toISOString()
    await render(<RelativeTime iso={iso} />)
    const time = document.querySelector('time')
    expect(time?.textContent).toBe(reference.format(-3, 'minute'))
    expect(time?.getAttribute('datetime')).toBe(iso)
  })

  it('gives the full date as its tooltip', async () => {
    const iso = '2026-06-01T10:00:00.000Z'
    await render(<RelativeTime iso={iso} />)
    expect(document.querySelector('time')?.getAttribute('title')).toBe(formatFull(iso))
  })
})
