import { currentUser, orvano } from '@/lib/orvano'
import { signOut } from '../actions'
import { Identities } from './identities'
import { ProviderButtons } from './provider-buttons'
import { providerErrorMessage } from './provider-errors'

/**
 * Sign in with Google, Apple, GitHub, or Microsoft, then link and unlink them. Turn each provider on
 * in the console's Sign in methods page first.
 */
export default async function Providers({
  searchParams,
}: {
  searchParams: Promise<{ orvano_error?: string | string[] }>
}) {
  // A provider sign in or link that didn't finish comes back with `orvano_error`.
  const { orvano_error: failed } = await searchParams
  const providerError = typeof failed === 'string' ? providerErrorMessage(failed) : null

  const user = await currentUser()
  if (user === null) {
    return (
      <section aria-labelledby="providers">
        <h1 id="providers">Sign in with a provider</h1>
        <ProviderButtons />
        {providerError !== null && <p role="alert">{providerError}</p>}
      </section>
    )
  }

  const { items } = await (await orvano()).account.listIdentities()
  return (
    <section aria-labelledby="signed-in">
      <h1 id="signed-in">You're signed in</h1>
      <p>Name: {user.name ?? 'No name'}</p>
      <p>Email: {user.email ?? 'No email'}</p>
      <form action={signOut}>
        <button type="submit">Sign out</button>
      </form>
      {providerError !== null && <p role="alert">{providerError}</p>}
      <Identities
        identities={items.map((i) => ({ id: i.id, provider: i.provider, email: i.email }))}
      />
    </section>
  )
}
