import { describe, expect, it } from 'vitest'

import { statusLabel } from './status.ts'

describe('statusLabel (AC-14, AC-18)', () => {
  it.each([
    ['active', 'Active', 'success'],
    ['provisioning', 'Setting up', 'warning'],
    ['failed', 'Failed', 'danger'],
    ['deleting', 'Deleting', 'neutral'],
  ] as const)('shows %s as the word "%s" with a %s tone', (status, label, tone) => {
    expect(statusLabel(status)).toEqual({ label, tone })
  })

  it('gives every status its own word, so color is never the only signal', () => {
    const labels = (['active', 'provisioning', 'failed', 'deleting'] as const).map(
      (status) => statusLabel(status).label,
    )
    expect(new Set(labels).size).toBe(labels.length)
  })
})
