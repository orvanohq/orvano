/** Your Orvano server, without `/v1`. */
export const endpoint = process.env.NEXT_PUBLIC_ORVANO_ENDPOINT ?? 'http://localhost:7700'

/** Your project's ID, from the project's overview in the Orvano console. */
export const project = process.env.NEXT_PUBLIC_ORVANO_PROJECT ?? ''
if (project === '') throw new Error('Set NEXT_PUBLIC_ORVANO_PROJECT in .env.local.')

/** This app's own URL. On plain http://localhost the session cookies skip `Secure`. */
export const appUrl = 'http://localhost:3000'
