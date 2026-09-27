import { request, type FullConfig } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

import { sessionFile, signIn } from './fixtures.ts'

/**
 * Signs the fixture console account in once for the whole run and keeps its cookies in a file every
 * worker reads: console sign in is limited to 10 per email per 15 minutes (spec 0004, rate limits).
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use.baseURL ?? 'http://localhost:8081'
  const context = await request.newContext()
  await writeFile(sessionFile, JSON.stringify(await signIn(context, baseURL)))
  await context.dispose()
}
