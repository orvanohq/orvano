import { expect, test as base, type APIRequestContext, type Page } from '@playwright/test'
import { readFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/** The first console account in tests/scenarios/fixtures.yaml: the install admin who owns the Fixtures org. */
export const consoleUser = {
  email: 'fixture-admin@example.com',
  password: 'fixture console password',
}

/** The seeded project the shared scenarios use. */
export const scenariosProject = 'scenarios0000000000a'

export { expect }

/** Where the global setup keeps the run's session cookies. */
export const sessionFile = join(tmpdir(), 'orvano-console-e2e-session.json')

/** One cookie the console's sign in set: name, value, and the path it is scoped to. */
export interface SessionCookie {
  name: string
  value: string
  path: string
}

/**
 * Signs the fixture console account in through the API, as the console page would (same origin
 * fetch metadata), and returns the session cookies it set (spec 0004, AC-27).
 */
export async function signIn(
  request: APIRequestContext,
  baseURL: string,
): Promise<SessionCookie[]> {
  const response = await request.post(`${baseURL}/v1/console/account/session`, {
    headers: { 'sec-fetch-site': 'same-origin' },
    data: consoleUser,
  })
  expect(response.status()).toBe(201)
  return response
    .headersArray()
    .filter((header) => header.name.toLowerCase() === 'set-cookie')
    .map((header) => {
      const [pair = '', ...attributes] = header.value.split(';').map((part) => part.trim())
      const [name = '', ...value] = pair.split('=')
      const path = attributes.find((a) => a.toLowerCase().startsWith('path='))?.slice(5) ?? '/'
      return { name, value: value.join('='), path }
    })
}

/** The Cookie header the signed in fixture account sends on a console call. */
export function cookieHeader(cookies: SessionCookie[]): string {
  return cookies
    .filter((cookie) => cookie.path === '/')
    .map((cookie) => `${cookie.name}=${cookie.value}`)
    .join('; ')
}

/** The ID of the seeded `Fixtures` org, read through the API as the fixture console account. */
export async function fixturesOrgId(
  request: APIRequestContext,
  baseURL: string,
  cookies: SessionCookie[],
): Promise<string> {
  const response = await request.get(`${baseURL}/v1/console/orgs`, {
    headers: { cookie: cookieHeader(cookies) },
  })
  const orgs = (await response.json()) as { items: { id: string; name: string }[] }
  const org = orgs.items.find((item) => item.name === 'Fixtures')
  if (org === undefined) throw new Error('the Fixtures org is missing')
  return org.id
}

interface Fixtures {
  /** A page that starts signed in as the fixture console account. */
  signedIn: Page
}

interface WorkerFixtures {
  /** The fixture account's session cookies, from the run's one sign in (e2e/global-setup.ts). */
  session: SessionCookie[]
}

export const test = base.extend<Fixtures, WorkerFixtures>({
  session: [
    // eslint-disable-next-line no-empty-pattern -- Playwright fixtures take their dependencies as an object.
    async ({}, use) => {
      await use(JSON.parse(await readFile(sessionFile, 'utf8')) as SessionCookie[])
    },
    { scope: 'worker' },
  ],
  signedIn: async ({ page, baseURL, session }, use) => {
    if (baseURL === undefined) throw new Error('baseURL is not set')
    const host = new URL(baseURL).hostname
    await page.context().addCookies(
      session.map((cookie) => ({
        ...cookie,
        domain: host,
        httpOnly: true,
        sameSite: 'Strict' as const,
      })),
    )
    await use(page)
  },
})
