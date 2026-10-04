'use client'

import { useActionState } from 'react'
import type { FormState } from '../actions'
import { unlinkIdentity } from './actions'
import { ProviderButtons, providers, type ProviderId } from './provider-buttons'

const ready: FormState = { error: null }

/** One linked provider account, as the page shows it. */
export interface LinkedIdentity {
  id: string
  provider: ProviderId
  email: string | null
}

/** The user's linked providers, each with Unlink, and a Link button for the others. */
export function Identities({ identities }: { identities: LinkedIdentity[] }) {
  const [state, unlink, unlinking] = useActionState(unlinkIdentity, ready)
  const linked = new Set(identities.map((i) => i.provider))
  const unlinked = providers.map((p) => p.id).filter((id) => !linked.has(id))

  return (
    <section aria-labelledby="identities">
      <h2 id="identities">Sign in methods</h2>
      {identities.length === 0 && <p>No providers linked yet.</p>}
      <ul style={{ paddingInlineStart: 0, listStyle: 'none' }}>
        {identities.map((identity) => (
          <li key={identity.id}>
            <form
              action={unlink}
              style={{ display: 'block', marginBlock: '0.5rem' }}
              aria-label={`Unlink ${nameOf(identity.provider)}`}
            >
              {nameOf(identity.provider)}: {identity.email ?? 'no email'}{' '}
              <input type="hidden" name="identityId" value={identity.id} />
              <button type="submit" disabled={unlinking}>
                Unlink
              </button>
            </form>
          </li>
        ))}
      </ul>
      {state.error !== null && <p role="alert">{state.error}</p>}
      {unlinked.length > 0 && <ProviderButtons link only={unlinked} />}
    </section>
  )
}

function nameOf(id: ProviderId): string {
  return providers.find((p) => p.id === id)?.name ?? id
}
