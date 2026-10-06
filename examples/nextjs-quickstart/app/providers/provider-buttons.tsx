'use client'

import { createBrowserClient, OrvanoError } from '@orvano/nextjs'
import { useState } from 'react'
import { endpoint, project } from '@/lib/config'

/** The four providers, each turned on in the console's Sign in methods page. */
export const providers = [
  { id: 'google', name: 'Google' },
  { id: 'apple', name: 'Apple' },
  { id: 'github', name: 'GitHub' },
  { id: 'microsoft', name: 'Microsoft' },
] as const

/** One of the providers above. */
export type ProviderId = (typeof providers)[number]['id']

/**
 * A button per provider. Signing in (or linking, with `link`) sends the browser to the provider;
 * it comes back through the route handler, which sets the session and returns to `/providers`.
 */
export function ProviderButtons({
  link = false,
  only = providers.map((p) => p.id),
}: {
  link?: boolean
  only?: readonly ProviderId[]
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // #region sign-in-with-provider
  async function go(provider: ProviderId) {
    setBusy(true)
    setError(null)
    try {
      const { client } = createBrowserClient({ endpoint, project })
      if (link) await client.linkIdentity(provider, { redirectUrl: '/providers' })
      else await client.signInWithOAuth(provider, { redirectUrl: '/providers' })
    } catch (failure) {
      // A refused start, like a provider that is off; the page stays.
      if (!(failure instanceof OrvanoError)) throw failure
      setError(failure.message)
      setBusy(false)
    }
  }
  // #endregion sign-in-with-provider

  return (
    <div style={{ display: 'grid', gap: '0.5rem', marginBlock: '1rem' }}>
      {providers
        .filter((p) => only.includes(p.id))
        .map((p) => (
          <button key={p.id} type="button" disabled={busy} onClick={() => void go(p.id)}>
            {link ? `Link ${p.name}` : `Sign in with ${p.name}`}
          </button>
        ))}
      {error !== null && <p role="alert">{error}</p>}
    </div>
  )
}
