import { expect, scenariosProject, test } from './fixtures.ts'

test('every console file carries the security headers (AC-24)', async ({ request }) => {
  for (const path of ['/', '/orgs', '/theme-init.js', '/favicon.svg']) {
    const response = await request.get(path)
    const headers = response.headers()
    expect(headers['content-security-policy'], path).toContain("default-src 'self'")
    expect(headers['content-security-policy'], path).toContain("frame-ancestors 'none'")
    expect(headers['content-security-policy'], path).not.toContain('unsafe-inline')
    expect(headers['x-content-type-options'], path).toBe('nosniff')
    expect(headers['x-frame-options'], path).toBe('DENY')
    expect(headers['referrer-policy'], path).toBe('strict-origin-when-cross-origin')
    expect(headers['permissions-policy'], path).toContain('camera=()')
    expect(headers['cross-origin-opener-policy'], path).toBe('same-origin')
  }
})

test('hashed assets cache for a year, and the shell revalidates (AC-24)', async ({ request }) => {
  const index = await request.get('/')
  expect(index.headers()['cache-control']).toBe('no-cache')
  expect((await request.get('/theme-init.js')).headers()['cache-control']).toBe('no-cache')
  const asset = /(?:src|href)="(\/assets\/[^"]+)"/.exec(await index.text())?.[1]
  if (asset === undefined) throw new Error('index.html references no /assets file')
  expect((await request.get(asset)).headers()['cache-control']).toBe(
    'public, max-age=31536000, immutable',
  )
})

test('a missing hashed asset is a 404 that is not cached for a year (AC-24)', async ({
  request,
}) => {
  const response = await request.get('/assets/does-not-exist-abc123.js')
  expect(response.status()).toBe(404)
  expect(response.headers()['cache-control'] ?? '').not.toContain('immutable')
  // The 404 is still a console file, so it keeps the security headers.
  expect(response.headers()['content-security-policy']).toContain("default-src 'self'")
  expect(response.headers()['x-content-type-options']).toBe('nosniff')
})

test('every shell page and select popup runs without a CSP violation (AC-24)', async ({
  signedIn: page,
}) => {
  await page.addInitScript(() => {
    const seen: string[] = []
    ;(window as unknown as { __csp: string[] }).__csp = seen
    document.addEventListener('securitypolicyviolation', (event) => {
      seen.push(`${event.violatedDirective}: ${event.blockedURI}`)
    })
  })
  const visited: string[] = []
  for (const path of ['/orgs', `/projects/${scenariosProject}`]) {
    await page.goto(path)
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await page.getByRole('button', { name: 'Account menu' }).click()
    await page.keyboard.press('Escape')
    visited.push(...(await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp)))
  }
  // A select popup is where Base UI injects its one `<style>` (see design.md's exception log).
  const selects = [
    { path: 'keys', open: 'Create key', select: 'Expiry', option: '30 days' },
    { path: 'platforms', open: 'Add platform', select: 'Type', option: 'Android' },
  ]
  for (const { path, open, select, option } of selects) {
    await page.goto(`/projects/${scenariosProject}/${path}`)
    await page.getByRole('button', { name: open }).first().click()
    await page.getByRole('dialog').getByRole('combobox', { name: select }).click()
    await expect(page.getByRole('option', { name: option })).toBeVisible()
    visited.push(...(await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp)))
  }
  expect(visited).toEqual([])
})
