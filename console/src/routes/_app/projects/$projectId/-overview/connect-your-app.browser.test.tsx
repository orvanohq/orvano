import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'

import { renderApp, setMode } from '@/test/app'
import {
  installFakeApi,
  makeKey,
  makeOrg,
  makePlatform,
  makeProject,
  type FakeApi,
} from '@/test/fake-api'

// Spec 0007, AC-21: the overview's Connect your app card.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'

function seed({ platform, key }: { platform: boolean; key: boolean }) {
  api.orgs = [makeOrg({ id: 'org00000000000000001' })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001' })]
  api.platforms = platform ? [makePlatform({ id: 'plat0000000000000001' })] : []
  api.apiKeys = key ? [makeKey({ id: 'key00000000000000001' })] : []
}

const steps = () =>
  [...document.querySelectorAll('ol li')].map((step) => ({
    text: step.textContent,
    link: step.querySelector('a')?.getAttribute('href'),
  }))

async function openOverview() {
  await renderApp(`/projects/${projectId}`)
  await expect.poll(() => steps().length).toBe(2)
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('Connect your app (AC-21)', () => {
  it('links each step to its page and shows neither done on a new project', async () => {
    seed({ platform: false, key: false })
    await openOverview()
    await new Promise((resolve) => setTimeout(resolve, 200))
    const [platformStep, keyStep] = steps()
    expect(platformStep.text).toContain('Add a platform')
    expect(platformStep.link).toBe(`/projects/${projectId}/platforms`)
    expect(keyStep.text).toContain('Create an API key')
    expect(keyStep.link).toBe(`/projects/${projectId}/keys`)
    expect(steps().some((step) => step.text.includes('Done'))).toBe(false)
  })

  it('marks a step Done, in words, once the project has a platform', async () => {
    seed({ platform: true, key: false })
    await openOverview()
    await expect.poll(() => steps()[0]?.text).toContain('Done')
    expect(steps()[1]?.text).not.toContain('Done')
  })

  it('marks both steps Done when the project has a platform and a key', async () => {
    seed({ platform: true, key: true })
    await openOverview()
    await expect.poll(() => steps().every((step) => step.text.includes('Done'))).toBe(true)
  })

  it.each(['dark', 'light'] as const)('has no axe violations, %s theme', async (theme) => {
    setMode(theme, 'comfortable')
    seed({ platform: true, key: false })
    await openOverview()
    await expect.poll(() => steps()[0]?.text).toContain('Done')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
