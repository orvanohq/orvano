import { describe, expect, it } from 'vitest'

import { safeRedirect } from './redirect.ts'

describe('safeRedirect (AC-20)', () => {
  it.each(['/orgs', '/projects/abc?tab=keys', '/a/b#c'])(
    'keeps the same origin path %s',
    (value) => {
      expect(safeRedirect(value)).toBe(value)
    },
  )

  it.each([
    '//evil.example',
    '/\\evil.example',
    'https://evil.example',
    'javascript:alert(1)',
    'evil.example',
    '',
    '/ok\nLocation: https://evil.example',
    undefined,
    42,
  ])('drops %j', (value) => {
    expect(safeRedirect(value)).toBeUndefined()
  })
})
