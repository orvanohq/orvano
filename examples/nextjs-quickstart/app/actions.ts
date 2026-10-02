'use server'

import { OrvanoError } from '@orvano/nextjs'
import { redirect } from 'next/navigation'
import { orvano } from '@/lib/orvano'

/** What a form shows after a failed try. */
export interface FormState {
  error: string | null
}

/** Creates the user and signs them in; Orvano sets the session cookies. */
export async function signUp(_state: FormState, form: FormData): Promise<FormState> {
  try {
    await (
      await orvano()
    ).account.create({
      name: field(form, 'name'),
      email: field(form, 'email'),
      password: field(form, 'password'),
    })
  } catch (error) {
    return failed(error)
  }
  redirect('/')
}

/** Signs an existing user in with their email and password. */
export async function signIn(_state: FormState, form: FormData): Promise<FormState> {
  try {
    await (
      await orvano()
    ).account.createPasswordSession({
      email: field(form, 'email'),
      password: field(form, 'password'),
    })
  } catch (error) {
    return failed(error)
  }
  redirect('/')
}

/** Ends this session on Orvano and clears the cookies. */
export async function signOut(): Promise<void> {
  await (await orvano()).account.deleteCurrentSession()
  redirect('/')
}

function field(form: FormData, name: string): string {
  const value = form.get(name)
  return typeof value === 'string' ? value : ''
}

/** Orvano's message for an expected failure (a taken email, a wrong password); anything else throws. */
function failed(error: unknown): FormState {
  if (error instanceof OrvanoError) return { error: error.message }
  throw error
}
