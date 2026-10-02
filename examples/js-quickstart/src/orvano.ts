import { Client, Orvano, OrvanoError, type User } from '@orvano/js'
import { endpoint, project } from './config'

/**
 * One Orvano client for the whole app. In a browser it keeps the session in localStorage, so a signed in user stays
 * signed in across reloads, and it refreshes the session before its access token runs out.
 */
export const orvano = new Orvano(new Client({ endpoint, project }))

/** The signed in user, or null when nobody is signed in. */
export async function currentUser(): Promise<User | null> {
  if ((await orvano.client.getSession()) === null) return null
  try {
    return await orvano.account.get()
  } catch (error) {
    if (error instanceof OrvanoError && error.status === 401) return null
    throw error
  }
}
