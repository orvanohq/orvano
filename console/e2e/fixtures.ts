import { expect, test as base, type APIRequestContext, type Page } from '@playwright/test'

/** The console session tests/scenarios/fixtures.yaml lists; the server accepts it in Test only. */
export const sessionCookie = { name: 'orvano_console', value: 'test-console-session' }

/** The seeded project the shared scenarios use. */
export const scenariosProject = 'scenarios0000000000a'

export { expect }

/** The ID of the seeded `Fixtures` org, read through the API as the fixture console user. */
export async function fixturesOrgId(request: APIRequestContext, baseURL: string): Promise<string> {
  const response = await request.get(`${baseURL}/v1/console/orgs`, {
    headers: { cookie: `${sessionCookie.name}=${sessionCookie.value}` },
  })
  const orgs = (await response.json()) as { items: { id: string; name: string }[] }
  const org = orgs.items.find((item) => item.name === 'Fixtures')
  if (org === undefined) throw new Error('the Fixtures org is missing')
  return org.id
}

interface Fixtures {
  /** A page that starts signed in as the fixture console user. */
  signedIn: Page
}

export const test = base.extend<Fixtures>({
  signedIn: async ({ page, baseURL }, use) => {
    if (baseURL === undefined) throw new Error('baseURL is not set')
    await page.context().addCookies([{ ...sessionCookie, url: baseURL }])
    await use(page)
  },
})
