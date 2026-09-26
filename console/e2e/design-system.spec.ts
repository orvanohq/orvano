import { expect, scenariosProject, test } from './fixtures.ts'

test('the first visit is dark with no flash, even when the OS prefers light (AC-2)', async ({
  signedIn: page,
}) => {
  // The attribute is set by theme-init.js before the first paint: check it as the document parses.
  await page.addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      ;(window as unknown as { __themeAtParse: string | null }).__themeAtParse =
        document.documentElement.getAttribute('data-theme')
    })
  })
  await page.goto('/orgs')
  expect(
    await page.evaluate(
      () => (window as unknown as { __themeAtParse: string | null }).__themeAtParse,
    ),
  ).toBe('dark')
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).not.toBe(
    'rgb(255, 255, 255)',
  )
})

test('System follows the OS live, and the choice survives a reload (AC-2)', async ({
  signedIn: page,
}) => {
  await page.goto('/orgs')
  await page.emulateMedia({ colorScheme: 'light' })
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitemradio', { name: 'System' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  expect(await page.evaluate(() => window.localStorage.getItem('orvano.theme'))).toBe('system')

  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitemradio', { name: 'Light' }).click()
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})

test('Comfortable density grows controls and rows, and survives a reload (AC-3)', async ({
  signedIn: page,
}) => {
  await page.goto(`/orgs`)
  await page.getByRole('combobox', { name: /^Switch org/ }).click()
  await page.getByRole('option').first().click()
  const row = page.locator('tbody tr').first()
  await expect(row).toBeVisible()
  expect((await row.boundingBox())?.height).toBe(36)
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitemradio', { name: 'Comfortable' }).click()
  expect((await row.boundingBox())?.height).toBe(44)
  expect((await page.getByRole('button', { name: 'Account menu' }).boundingBox())?.height).toBe(40)
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-density', 'comfortable')
})

test('a dialog does not animate with reduced motion (AC-7)', async ({ signedIn: page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto('/orgs')
  await page.getByRole('button', { name: 'Account menu' }).click()
  const menu = page.getByRole('menu')
  await expect(menu).toBeVisible()
  const durations = await menu.evaluate((element) => {
    const style = getComputedStyle(element)
    return [style.animationDuration, style.transitionDuration]
  })
  expect(
    durations.every((value) => value.split(',').every((part) => Number.parseFloat(part) === 0)),
  ).toBe(true)
})

test('the console loads nothing from another host (AC-8)', async ({ signedIn: page, baseURL }) => {
  const hosts = new Set<string>()
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.protocol.startsWith('http')) hosts.add(url.host)
  })
  await page.goto(`/projects/${scenariosProject}`)
  await expect(page.getByRole('heading', { level: 1, name: 'Scenarios' })).toBeVisible()
  await page.goto('/orgs')
  await expect(page.getByRole('heading', { level: 1, name: 'Orgs' })).toBeVisible()
  expect([...hosts]).toEqual([new URL(baseURL ?? '').host])
  // Inter and JetBrains Mono are bundled, so the fonts came from this origin too.
  expect(await page.evaluate(() => document.fonts.check('14px "Inter Variable"'))).toBe(true)
})

test('the catalog is absent from the production build (AC-9)', async ({
  signedIn: page,
  request,
}) => {
  await page.goto('/dev/components')
  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible()
  const index = await (await request.get('/')).text()
  const scripts = [...index.matchAll(/src="(\/assets\/[^"]+\.js)"/g)].map((match) => match[1])
  for (const script of scripts) {
    expect(await (await request.get(script)).text()).not.toContain('orvano-dev-catalog')
  }
})
