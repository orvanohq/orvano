import axe from 'axe-core'
import { beforeEach, describe, expect, it } from 'vitest'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, type FakeApi } from '@/test/fake-api'

// Spec 0006 AC-23: while the install waits for its first admin, /sign-in points you to the setup link
// the installer printed, in place of the form.

const api: FakeApi = installFakeApi()

beforeEach(() => {
  api.requests = []
})

describe('/sign-in', () => {
  it('shows the finish setup notice and no form while setup is required', async () => {
    api.setupRequired = true

    const { screen } = await renderApp('/sign-in')

    await expect.element(screen.getByText('Finish setting up Orvano')).toBeVisible()
    await expect
      .element(screen.getByText('Open the setup link the installer printed on your server.'))
      .toBeVisible()
    expect(document.querySelector('input[type=password]')).toBeNull()
    expect(document.querySelector('form')).toBeNull()
  })

  it('shows the sign in form once the install has its admin', async () => {
    api.setupRequired = false

    const { screen } = await renderApp('/sign-in')

    await expect.element(screen.getByLabelText('Password')).toBeVisible()
    expect(document.body.textContent).not.toContain('Finish setting up Orvano')
  })

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode with the setup notice showing`, async () => {
      setMode(theme, 'comfortable')
      api.setupRequired = true
      const { screen } = await renderApp('/sign-in')
      await expect.element(screen.getByText('Finish setting up Orvano')).toBeVisible()

      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })

      expect(results.violations.map((violation) => violation.id)).toEqual([])
    })
  }
})
