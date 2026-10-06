/** What the page says when a provider redirect came back with `orvano_error`. */
export function providerErrorMessage(code: string): string {
  switch (code) {
    case 'oauth_access_denied':
      return 'Sign in was cancelled at the provider.'
    case 'provider_already_linked':
      return 'This account already has another account of that provider linked.'
    case 'identity_already_linked':
      return 'That provider account belongs to another user.'
    case 'reauthentication_required':
      return 'Sign in again, then link the provider within 10 minutes.'
    default:
      return `Sign in didn't finish (${code}). Try again.`
  }
}
