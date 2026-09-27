import { describe, expect, it } from 'vitest'
import { render } from 'vitest-browser-react'

import { usePageTitle } from '@/lib/page-title'

function Title({ page, name }: { page: string; name?: string }) {
  usePageTitle(page, name)
  return null
}

describe('usePageTitle (AC-23)', () => {
  it('sets the title to page, name, and product, in that order', async () => {
    await render(<Title page="Overview" name="Scenarios" />)
    expect(document.title).toBe('Overview · Scenarios · Orvano')
  })

  it('leaves the name out when there is none yet', async () => {
    await render(<Title page="Orgs" />)
    expect(document.title).toBe('Orgs · Orvano')
  })

  it('leaves the name out when it is empty', async () => {
    await render(<Title page="Projects" name="" />)
    expect(document.title).toBe('Projects · Orvano')
  })

  it('follows the name once it loads', async () => {
    const screen = await render(<Title page="Projects" />)
    expect(document.title).toBe('Projects · Orvano')
    await screen.rerender(<Title page="Projects" name="Fixtures" />)
    expect(document.title).toBe('Projects · Fixtures · Orvano')
  })
})
