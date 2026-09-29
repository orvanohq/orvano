import { describe, expect, it } from 'vitest'

import { memberName, userRefName } from './member-name.ts'

describe('memberName (AC-15)', () => {
  it('uses the name when the member has one', () => {
    expect(memberName({ name: 'Grace Hopper', email: 'grace@example.com' })).toBe('Grace Hopper')
  })

  it('falls back to the part of the email before @', () => {
    expect(memberName({ name: null, email: 'linus@example.com' })).toBe('linus')
  })

  it('keeps a plus tag and dots in that part', () => {
    expect(memberName({ name: null, email: 'grace.h+1@example.com' })).toBe('grace.h+1')
  })
})

describe('userRefName (AC-18, AC-25)', () => {
  it('uses the name when the account has one', () => {
    expect(userRefName({ id: 'user1', name: 'Ada', email: 'ada@example.com' })).toBe('Ada')
  })

  it('falls back to the whole email when there is no name', () => {
    expect(userRefName({ id: 'user1', name: null, email: 'ada@example.com' })).toBe(
      'ada@example.com',
    )
  })

  it('says "Deleted account" once the account is gone', () => {
    expect(userRefName(null)).toBe('Deleted account')
  })
})
