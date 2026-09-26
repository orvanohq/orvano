import { describe, expect, it } from 'vitest'

import { sortActiveFirst } from './sort.ts'

describe('sortActiveFirst (AC-13)', () => {
  it('puts deleting items after active ones and keeps the API order inside each group', () => {
    const items = [
      { id: 'a', status: 'deleting' },
      { id: 'b', status: 'active' },
      { id: 'c', status: 'deleting' },
      { id: 'd', status: 'active' },
      { id: 'e', status: 'provisioning' },
    ]
    expect(sortActiveFirst(items).map((item) => item.id)).toEqual(['b', 'd', 'e', 'a', 'c'])
  })

  it('sorts the whole loaded list again after a later page, not one page at a time', () => {
    const page1 = [
      { id: '1', status: 'active' },
      { id: '2', status: 'deleting' },
    ]
    const page2 = [
      { id: '3', status: 'active' },
      { id: '4', status: 'deleting' },
    ]
    expect(sortActiveFirst([...page1, ...page2]).map((item) => item.id)).toEqual([
      '1',
      '3',
      '2',
      '4',
    ])
  })
})
