import { describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { PageHeading } from '@/shell/page-heading'

describe('PageHeading (AC-23)', () => {
  it('renders the page title as the h1 with the id the router focuses', async () => {
    await render(<PageHeading>Projects</PageHeading>)
    const heading = document.querySelector('h1')
    expect(heading?.id).toBe('page-title')
    expect(heading?.textContent).toBe('Projects')
  })

  it('can take focus from code, so a screen reader announces the new page', async () => {
    await render(<PageHeading>Projects</PageHeading>)
    const heading = document.querySelector<HTMLElement>('#page-title')
    heading?.focus()
    expect(document.activeElement).toBe(heading)
  })

  it('stays out of the tab order, so it is not one more stop for keyboard users', async () => {
    await render(
      <>
        <button>Before</button>
        <PageHeading>Projects</PageHeading>
        <button>After</button>
      </>,
    )
    await userEvent.tab()
    expect(document.activeElement?.textContent).toBe('Before')
    await userEvent.tab()
    expect(document.activeElement?.textContent).toBe('After')
  })
})
