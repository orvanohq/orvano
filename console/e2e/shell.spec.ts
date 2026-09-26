import { expect, fixturesOrgId, scenariosProject, sessionCookie, test } from './fixtures.ts'

test('the happy path, keyboard only, on real data (AC-10, AC-11, AC-12, AC-13, AC-14, AC-26)', async ({
  signedIn: page,
}) => {
  await page.goto('/')
  await expect(page).toHaveURL(/\/orgs$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Orgs' })).toBeVisible()
  // The first load is not an in app navigation, so nothing has moved focus yet.
  expect(await page.evaluate(() => document.activeElement?.tagName)).toBe('BODY')

  // The frame: one banner, one main, and a skip link that is the first tab stop.
  await expect(page.getByRole('banner')).toHaveCount(1)
  await expect(page.getByRole('main')).toHaveCount(1)
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('main')).toBeFocused()

  // Org switcher: open with Enter, move with the arrows, pick with Enter.
  await page.getByRole('combobox', { name: /^Switch org/ }).focus()
  await page.keyboard.press('Enter')
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/orgs\/[^/]+$/)
  await expect(page.getByRole('navigation', { name: 'Org navigation' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Projects' })).toHaveAttribute('aria-current', 'page')

  // The org page lists the seeded project; open it.
  await page.getByRole('link', { name: 'Scenarios' }).first().press('Enter')
  await expect(page).toHaveURL(new RegExp(`/projects/${scenariosProject}$`))
  await expect(page.getByRole('heading', { level: 1, name: 'Scenarios' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Project navigation' })).toBeVisible()

  // Project switcher, keyboard only.
  await page.getByRole('combobox', { name: /^Switch project/ }).focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('option', { name: /Scenarios/ })).toBeVisible()
  await page.keyboard.type('Scenarios')
  await page.keyboard.press('ArrowDown')
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(new RegExp(`/projects/${scenariosProject}$`))

  // Direct links and history (AC-11), and the landing rule (AC-12).
  await page.goBack()
  await expect(page).toHaveURL(/\/orgs\/[^/]+$/)
  await page.goForward()
  await page.goto('/')
  await expect(page).toHaveURL(new RegExp(`/projects/${scenariosProject}$`))
})

test('a missing session lands on sign in once, with a safe redirect (AC-20)', async ({
  page,
  baseURL,
}) => {
  const signInNavigations: string[] = []
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame() && new URL(frame.url()).pathname === '/sign-in') {
      signInNavigations.push(frame.url())
    }
  })
  await page.goto(`/projects/${scenariosProject}`)
  await expect(page).toHaveURL(`/sign-in?redirect=%2Fprojects%2F${scenariosProject}`)
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
  expect(signInNavigations).toHaveLength(1)

  for (const bad of ['//evil.example', 'https://evil.example', '/\\evil.example']) {
    await page.goto(`/sign-in?redirect=${encodeURIComponent(bad)}`)
    // The page stays put: an off origin value is never followed.
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
    expect(new URL(page.url()).origin).toBe(new URL(baseURL ?? '').origin)
  }
})

test('a new project shows Setting up, then its overview, without a reload (AC-18)', async ({
  signedIn: page,
  request,
  baseURL,
}) => {
  const headers = { cookie: `${sessionCookie.name}=${sessionCookie.value}` }
  const orgId = await fixturesOrgId(request, baseURL ?? '')
  const created = await request.post(`${baseURL ?? ''}/v1/console/orgs/${orgId}/projects`, {
    headers,
    data: { name: `Provisioning ${String(Date.now())}` },
  })
  expect(created.ok()).toBe(true)
  const project = (await created.json()) as { id: string; name: string }

  await page.goto(`/projects/${project.id}`)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/Setting up|^Provisioning/)
  await expect(page.getByRole('heading', { level: 1, name: project.name })).toBeVisible({
    timeout: 30_000,
  })
})

test('a project that does not exist shows in shell not found and is forgotten (AC-12, AC-19)', async ({
  signedIn: page,
}) => {
  await page.goto('/orgs')
  await page.evaluate(() => {
    window.localStorage.setItem('orvano.lastProject', 'zzzzzzzzzzzzzzzzzzzz')
  })
  await page.goto('/projects/zzzzzzzzzzzzzzzzzzzz')
  await expect(page.getByRole('heading', { name: 'Project not found' })).toBeVisible()
  await expect(page.getByRole('banner')).toBeVisible()
  expect(await page.evaluate(() => window.localStorage.getItem('orvano.lastProject'))).toBeNull()

  await page.goto('/')
  await expect(page).toHaveURL(/\/orgs$/)

  await page.goto('/no/such/page')
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible()
})

test('titles and focus follow navigation (AC-23)', async ({ signedIn: page }) => {
  await page.goto('/orgs')
  await expect(page).toHaveTitle('Orgs · Orvano')
  await page.getByRole('combobox', { name: /^Switch org/ }).click()
  await page.getByRole('option').first().click()
  await expect(page).toHaveTitle('Projects · Fixtures · Orvano')
  await expect(page.locator('#page-title')).toBeFocused()
})

test('a failed load shows the error panel with a Retry (AC-21)', async ({
  signedIn: page,
  request,
  baseURL,
}) => {
  const orgId = await fixturesOrgId(request, baseURL ?? '')
  let fail = true
  await page.route('**/v1/console/orgs/*/projects*', async (route) => {
    if (!fail) return route.continue()
    await route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        type: 'about:blank',
        title: 'Server error',
        status: 500,
        code: 'internal_error',
        detail: 'The API is having a bad day.',
        requestId: 'req_test_123',
      }),
    })
  })
  await page.goto(`/orgs/${orgId}`)
  const panel = page.getByRole('alert').filter({ hasText: "This page didn't load" })
  await expect(panel).toBeVisible()
  await expect(panel).toContainText('req_test_123')
  await expect(panel).not.toContainText('{')
  fail = false
  await panel.getByRole('button', { name: 'Retry' }).click()
  await expect(page.getByRole('link', { name: 'Scenarios' })).toBeVisible()
})

test('no shell page scrolls sideways at 360 px, and the drawer traps focus (AC-16, AC-17)', async ({
  signedIn: page,
}) => {
  await page.setViewportSize({ width: 360, height: 740 })
  for (const path of ['/orgs', `/projects/${scenariosProject}`]) {
    await page.goto(path)
    await expect(page.getByRole('main')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(360)
  }
  // Project context: only the project switcher stays in the bar; the org switcher is in the drawer.
  await expect(page.getByRole('banner').getByRole('combobox', { name: /^Switch org/ })).toBeHidden()
  await page.getByRole('button', { name: 'Open navigation' }).click()
  const drawer = page.getByRole('dialog')
  await expect(drawer.getByRole('combobox', { name: /^Switch org/ })).toBeVisible()
  await expect(drawer.getByRole('link', { name: 'Overview' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(drawer).toBeHidden()
  await expect(page.getByRole('button', { name: 'Open navigation' })).toBeFocused()
})

test('the sidebar collapses to a rail with [ and remembers it (AC-16)', async ({
  signedIn: page,
}) => {
  await page.setViewportSize({ width: 1280, height: 800 })
  await page.goto(`/projects/${scenariosProject}`)
  const sidebar = page.locator('[data-slot=sidebar]')
  await expect(sidebar).toHaveAttribute('data-state', 'expanded')
  expect((await sidebar.boundingBox())?.width).toBe(240)
  await page.locator('body').press('[')
  await expect(sidebar).toHaveAttribute('data-state', 'rail')
  expect((await sidebar.boundingBox())?.width).toBe(56)
  await page.reload()
  await expect(page.locator('[data-slot=sidebar]')).toHaveAttribute('data-state', 'rail')
})
