import { afterEach, describe, expect, it, vi } from 'vitest'

import { ErrorCode, decodeEvent, eventRegistry, paginate } from '../src/index.js'
import type { Page } from '../src/index.js'
import { Client as ServerClient } from '../src/server.js'

describe('paginate (AC-7)', () => {
  it('walks every page in order, passing each nextCursor to the next call', async () => {
    const pages: Record<string, Page<number>> = {
      start: { items: [1, 2], nextCursor: 'b' },
      b: { items: [3, 4], nextCursor: 'c' },
      c: { items: [5], nextCursor: null },
    }
    const cursors: (string | undefined)[] = []

    const items: number[] = []
    for await (const item of paginate((cursor) => {
      cursors.push(cursor)
      return Promise.resolve(pages[cursor ?? 'start'] ?? { items: [], nextCursor: null })
    })) {
      items.push(item)
    }

    expect(items).toEqual([1, 2, 3, 4, 5])
    expect(cursors).toEqual([undefined, 'b', 'c'])
  })

  it('stops after an empty last page', async () => {
    const items: number[] = []
    for await (const item of paginate<number>(() =>
      Promise.resolve({ items: [], nextCursor: null }),
    ))
      items.push(item)

    expect(items).toEqual([])
  })
})

describe('decodeEvent (AC-8)', () => {
  it('returns undefined for an event this SDK does not know', () => {
    expect(decodeEvent('nobody.knows', { a: 1 })).toBeUndefined()
  })

  it('decodes a known event through the registry it is given', () => {
    const registry = { ...eventRegistry, 'things.created': (raw: unknown) => ({ decoded: raw }) }

    expect(decodeEvent('things.created', { id: 't1' }, registry)).toEqual({ decoded: { id: 't1' } })
  })

  it('refuses a payload that is not a JSON object', () => {
    const registry = { 'things.created': (raw: unknown) => raw }

    expect(() => decodeEvent('things.created', [1, 2], registry)).toThrow(TypeError)
  })

  it('does not treat inherited object keys as events', () => {
    expect(decodeEvent('toString', {})).toBeUndefined()
  })
})

describe('the server entry in a browser (AC-4)', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('throws when an API key is set in a browser', () => {
    vi.stubGlobal('document', {})

    expect(() => new ServerClient({ endpoint: 'https://orvano.example.com', apiKey: 'k' })).toThrow(
      /trusted server code only/,
    )
  })

  it('accepts a key outside a browser', () => {
    expect(
      () => new ServerClient({ endpoint: 'https://orvano.example.com', apiKey: 'k' }),
    ).not.toThrow()
  })
})

describe('error codes (AC-6)', () => {
  it('exposes the generated catalog as constants', () => {
    expect(ErrorCode.notFound).toBe('not_found')
    expect(ErrorCode.internalError).toBe('internal_error')
  })
})
