import { createServerClient, OrvanoError } from '@orvano/nextjs'
import { cookies, headers } from 'next/headers'
import { appUrl, endpoint, project } from './config'

/** An Orvano client for this request. The user's session lives in its cookies. */
export async function orvano() {
  return createServerClient({
    endpoint,
    project,
    appUrl,
    cookies: await cookies(),
    requestHeaders: await headers(),
  })
}

/** The signed in user, or null when nobody is signed in. */
export async function currentUser() {
  try {
    return await (await orvano()).account.get()
  } catch (error) {
    if (error instanceof OrvanoError && error.status === 401) return null
    throw error
  }
}
