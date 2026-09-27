import axe from 'axe-core'
import { describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { InShellNotFound } from '@/shell/in-shell-not-found'
import { renderInRouter } from '@/test/router'

const paths = ['/orgs'] as const
const text = () => document.body.textContent

describe('InShellNotFound (AC-19)', () => {
  it('says a project does not exist or is not yours to open', async () => {
    await renderInRouter(<InShellNotFound what="project" />, { at: '/', paths })
    expect(document.querySelector('h1')?.textContent).toBe('Project not found')
    expect(text()).toContain("This project doesn't exist or you don't have access to it.")
  })

  it('says the same for an org', async () => {
    await renderInRouter(<InShellNotFound what="org" />, { at: '/', paths })
    expect(document.querySelector('h1')?.textContent).toBe('Org not found')
    expect(text()).toContain("This org doesn't exist or you don't have access to it.")
  })

  it('titles the page after what is missing', async () => {
    await renderInRouter(<InShellNotFound what="project" />, { at: '/', paths })
    expect(document.title).toBe('Project not found · Orvano')
  })

  it('renders its heading as the page heading', async () => {
    await renderInRouter(<InShellNotFound what="project" />, { at: '/', paths })
    expect(document.querySelectorAll('h1#page-title')).toHaveLength(1)
  })

  it('offers a way back to the org list, and it works', async () => {
    const { router } = await renderInRouter(<InShellNotFound what="project" />, {
      at: '/',
      paths,
    })
    const link = document.querySelector('a')
    expect(link?.textContent).toBe('Go to orgs')
    expect(link?.getAttribute('href')).toBe('/orgs')

    await userEvent.click(link ?? document.body)

    await expect.poll(() => router.state.location.pathname).toBe('/orgs')
  })

  it('has no axe violations', async () => {
    await renderInRouter(
      <main>
        <InShellNotFound what="project" />
      </main>,
      { at: '/', paths },
    )
    const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
