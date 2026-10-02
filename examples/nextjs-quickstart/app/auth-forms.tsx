'use client'

import { useActionState } from 'react'
import { signIn, signUp, type FormState } from './actions'

const ready: FormState = { error: null }

/** A sign up form and a sign in form, each showing Orvano's message when it fails. */
export function AuthForms() {
  const [signUpState, signUpAction, signingUp] = useActionState(signUp, ready)
  const [signInState, signInAction, signingIn] = useActionState(signIn, ready)

  return (
    <>
      <h1>Orvano Next.js quickstart</h1>
      <form action={signUpAction} aria-labelledby="sign-up">
        <h2 id="sign-up">Sign up</h2>
        <label>
          Name <input name="name" autoComplete="name" />
        </label>
        <label>
          Email <input name="email" type="email" autoComplete="email" required />
        </label>
        <label>
          Password <input name="password" type="password" autoComplete="new-password" required />
        </label>
        <button type="submit" disabled={signingUp}>
          Sign up
        </button>
        {signUpState.error !== null && <p role="alert">{signUpState.error}</p>}
      </form>

      <form action={signInAction} aria-labelledby="sign-in">
        <h2 id="sign-in">Sign in</h2>
        <label>
          Email <input name="email" type="email" autoComplete="email" required />
        </label>
        <label>
          Password{' '}
          <input name="password" type="password" autoComplete="current-password" required />
        </label>
        <button type="submit" disabled={signingIn}>
          Sign in
        </button>
        {signInState.error !== null && <p role="alert">{signInState.error}</p>}
      </form>
    </>
  )
}
