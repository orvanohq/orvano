import { afterEach, describe, expect, it, vi } from 'vitest'

import { getRememberedLinkUrl, rememberLinkUrl, suggestedLinkUrls } from './link-url.ts'

// Spec 0010 AC-23: the send dialogs remember the last "Link opens at" URL per project, with every
// storage read and write wrapped so blocked storage never breaks the dialog, and hint the project's
// web platforms. These run in Node, so each test hands the module the `window` it needs.

/** A `window` whose `localStorage` is a plain map, like a browser that allows storage. */
function allowedStorage(): Map<string, string> {
  const items = new Map<string, string>()
  vi.stubGlobal('window', {
    localStorage: {
      getItem: (key: string) => items.get(key) ?? null,
      setItem: (key: string, value: string) => items.set(key, value),
    },
  })
  return items
}

/** A `window` whose `localStorage` throws on every call, as a blocked or private window can. */
function blockedStorage(): void {
  const blocked = () => {
    throw new DOMException('The operation is insecure.', 'SecurityError')
  }
  vi.stubGlobal('window', { localStorage: { getItem: blocked, setItem: blocked } })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('the remembered link URL (AC-23)', () => {
  it('starts empty, then returns the URL last sent for that project', () => {
    allowedStorage()

    expect(getRememberedLinkUrl('projecta')).toBe('')
    rememberLinkUrl('projecta', 'http://localhost:3000/auth/callback')
    rememberLinkUrl('projecta', 'https://app.example.com/auth')

    expect(getRememberedLinkUrl('projecta')).toBe('https://app.example.com/auth')
  })

  it('keeps each project separate', () => {
    allowedStorage()

    rememberLinkUrl('projecta', 'https://a.example.com/cb')

    expect(getRememberedLinkUrl('projectb')).toBe('')
    expect(getRememberedLinkUrl('projecta')).toBe('https://a.example.com/cb')
  })

  it('reads as empty and forgets quietly when storage is blocked', () => {
    blockedStorage()

    expect(() => {
      rememberLinkUrl('projecta', 'https://app.example.com/cb')
    }).not.toThrow()
    expect(getRememberedLinkUrl('projecta')).toBe('')
  })

  it('reads as empty when there is no storage at all', () => {
    vi.stubGlobal('window', {})

    expect(getRememberedLinkUrl('projecta')).toBe('')
    expect(() => {
      rememberLinkUrl('projecta', 'https://app.example.com/cb')
    }).not.toThrow()
  })
})

describe('suggestedLinkUrls (AC-23)', () => {
  it('suggests https for a web host and http only for localhost and 127.0.0.1', () => {
    expect(suggestedLinkUrls(['app.example.com', 'localhost', '127.0.0.1'])).toEqual([
      'https://app.example.com/',
      'http://localhost/',
      'http://127.0.0.1/',
    ])
  })

  it('leaves out wildcard hosts, which name no single host', () => {
    expect(suggestedLinkUrls(['*.example.com', 'app.example.com'])).toEqual([
      'https://app.example.com/',
    ])
  })

  it('suggests nothing for a project with no web platforms', () => {
    expect(suggestedLinkUrls([])).toEqual([])
  })
})
