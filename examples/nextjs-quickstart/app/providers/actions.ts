'use server'

import { OrvanoError } from '@orvano/nextjs'
import { redirect } from 'next/navigation'
import { orvano } from '@/lib/orvano'
import type { FormState } from '../actions'

/** Unlinks one provider; Orvano refuses the user's last way to sign in. */
export async function unlinkIdentity(_state: FormState, form: FormData): Promise<FormState> {
  const identityId = form.get('identityId')
  try {
    await (await orvano()).account.deleteIdentity(typeof identityId === 'string' ? identityId : '')
  } catch (error) {
    if (error instanceof OrvanoError) return { error: error.message }
    throw error
  }
  redirect('/providers')
}
