import { describe, expect, it } from 'vitest'

import { checkWebIdentifier, platformTypes, reduceWebIdentifier } from './platform-types.ts'

describe('reduceWebIdentifier (spec 0007, AC-19)', () => {
  it('keeps only the lowercase hostname of a pasted URL', () => {
    expect(reduceWebIdentifier('https://App.example.com:3000/login')).toBe('app.example.com')
  })

  it('drops a path and a port without a scheme', () => {
    expect(reduceWebIdentifier('localhost:5173/app')).toBe('localhost')
    expect(reduceWebIdentifier('Example.COM')).toBe('example.com')
  })

  it('leaves a wildcard pattern as it is', () => {
    expect(reduceWebIdentifier('*.example.com')).toBe('*.example.com')
  })
})

describe('checkWebIdentifier (spec 0003, web origin matching)', () => {
  it.each(['app.example.com', 'localhost', '127.0.0.1', '*.example.com', 'intranet'])(
    'accepts %s',
    (value) => {
      expect(checkWebIdentifier(value)).toBeUndefined()
    },
  )

  it.each([
    '*',
    '*.com',
    'a.*.example.com',
    '*.127.0.0.1',
    '256.1.1.1',
    'app_1.example.com',
    '-a.com',
  ])('refuses %s', (value) => {
    expect(checkWebIdentifier(value)).toBeTypeOf('string')
  })
})

describe('app identifiers (spec 0003, platform identifiers)', () => {
  it('checks Android package names', () => {
    expect(platformTypes.android.check('com.example.app')).toBeUndefined()
    expect(platformTypes.android.check('com')).toBeTypeOf('string')
    expect(platformTypes.android.check('com.1example')).toBeTypeOf('string')
  })

  it('checks Apple bundle IDs', () => {
    expect(platformTypes.ios.check('com.example-co.App')).toBeUndefined()
    expect(platformTypes.macos.check('app')).toBeTypeOf('string')
  })

  it('accepts any desktop label without whitespace', () => {
    expect(platformTypes.windows.check('my-app')).toBeUndefined()
    expect(platformTypes.linux.check('my app')).toBeTypeOf('string')
    expect(platformTypes.linux.check('x'.repeat(256))).toBeTypeOf('string')
  })
})
