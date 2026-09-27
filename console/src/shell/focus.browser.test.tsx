import type { AnyRouter } from '@tanstack/react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import { focusPageTitleOnNavigate } from '@/shell/focus'

type Listener = (event: { pathChanged: boolean }) => void

/** A router reduced to the one subscription the focus code uses, so a test decides when a page resolves. */
function fakeRouter() {
  let listener: Listener | undefined
  const router = {
    subscribe: (_event: string, callback: Listener) => {
      listener = callback
      return () => undefined
    },
  } as unknown as AnyRouter
  return {
    router,
    resolve: (pathChanged: boolean) => {
      listener?.({ pathChanged })
    },
  }
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 30))

beforeEach(() => {
  document.body.innerHTML =
    '<button id="elsewhere">Elsewhere</button><h1 id="page-title" tabindex="-1">Title</h1>'
  document.getElementById('elsewhere')?.focus()
})

afterEach(() => {
  document.body.innerHTML = ''
})

describe('focusPageTitleOnNavigate (AC-23)', () => {
  it('leaves focus where the browser put it on the first load', async () => {
    const { router, resolve } = fakeRouter()
    focusPageTitleOnNavigate(router)

    resolve(true)
    await settle()

    expect(document.activeElement?.id).toBe('elsewhere')
  })

  it('moves focus to the page title after an in app navigation', async () => {
    const { router, resolve } = fakeRouter()
    focusPageTitleOnNavigate(router)
    resolve(true)

    resolve(true)

    await expect.poll(() => document.activeElement?.id).toBe('page-title')
  })

  it('leaves focus alone when only the search or hash changed', async () => {
    const { router, resolve } = fakeRouter()
    focusPageTitleOnNavigate(router)
    resolve(true)

    resolve(false)
    await settle()

    expect(document.activeElement?.id).toBe('elsewhere')
  })

  it('does nothing when the new page has no title', async () => {
    const { router, resolve } = fakeRouter()
    focusPageTitleOnNavigate(router)
    resolve(true)
    document.getElementById('page-title')?.remove()

    resolve(true)
    await settle()

    expect(document.activeElement?.id).toBe('elsewhere')
  })
})
