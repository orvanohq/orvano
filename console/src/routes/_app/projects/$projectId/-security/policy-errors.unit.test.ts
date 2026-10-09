import { OrvanoError } from '@orvano/console-client'
import { describe, expect, it } from 'vitest'

import { linesLabel, placePolicyError, readLines, readWholeNumber } from './policy-errors.ts'

// Spec 0014 AC-1 and AC-34: a refused save lands on the field it names, and a list's zero based
// positions become the lines you typed.

const invalid = (detail: string) => new OrvanoError(400, 'invalid_request', detail, 'req')

describe('placePolicyError', () => {
  it('names the textarea lines of bad list entries, skipping blank lines', () => {
    const lines = readLines('ok.example\n\n*.example.com\nfine.example\na@b.example\n')
    expect(lines.entries).toEqual(['ok.example', '*.example.com', 'fine.example', 'a@b.example'])
    const placed = placePolicyError(
      invalid(
        'blockedEmailDomains has bad entries at positions 1, 3: each must be a domain such as example.com.',
      ),
      ['blockedEmailDomains'],
      { blockedEmailDomains: lines },
    )
    expect(placed).toEqual({
      field: 'blockedEmailDomains',
      message: 'Lines 3 and 5: Each must be a domain such as example.com.',
    })
  })

  it('places a bound on its field, and leaves other fields and errors to the alert', () => {
    const fields = ['passwordMinLength', 'signInFailedPerEmailIp']
    expect(placePolicyError(invalid('passwordMinLength must be from 8 to 64.'), fields)).toEqual({
      field: 'passwordMinLength',
      message: 'passwordMinLength must be from 8 to 64.',
    })
    expect(
      placePolicyError(invalid('signInFailedPerEmailIp.limit must be from 3 to 100.'), fields)
        ?.field,
    ).toBe('signInFailedPerEmailIp')
    expect(placePolicyError(invalid('sessionIdleSeconds must be at most x.'), fields)).toBeNull()
    expect(
      placePolicyError(new OrvanoError(409, 'email_not_configured', 'No SMTP.', null), fields),
    ).toBeNull()
    expect(placePolicyError(new Error('network'), fields)).toBeNull()
  })
})

describe('helpers', () => {
  it('lists lines in plain words', () => {
    expect(linesLabel([2])).toBe('Line 2')
    expect(linesLabel([1, 3])).toBe('Lines 1 and 3')
    expect(linesLabel([1, 2, 5])).toBe('Lines 1, 2, and 5')
  })

  it('reads whole numbers inside their bounds only', () => {
    expect(readWholeNumber(' 12 ', 8, 64)).toBe(12)
    expect(readWholeNumber('7', 8, 64)).toBeNull()
    expect(readWholeNumber('12.5', 8, 64)).toBeNull()
    expect(readWholeNumber('', 8, 64)).toBeNull()
    expect(readWholeNumber('-9', 8, 64)).toBeNull()
  })
})
