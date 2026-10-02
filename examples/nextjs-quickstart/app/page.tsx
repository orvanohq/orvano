import { currentUser } from '@/lib/orvano'
import { signOut } from './actions'
import { AuthForms } from './auth-forms'

/** Shows the signed in user, or the sign up and sign in forms. */
export default async function Home() {
  const user = await currentUser()
  if (user === null) return <AuthForms />

  return (
    <section aria-labelledby="signed-in">
      <h1 id="signed-in">You're signed in</h1>
      <p>Name: {user.name ?? 'No name'}</p>
      <p>Email: {user.email}</p>
      <form action={signOut}>
        <button type="submit">Sign out</button>
      </form>
    </section>
  )
}
