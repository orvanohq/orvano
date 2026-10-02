/** Where Orvano runs; the local stack by default. */
export const endpoint = import.meta.env.VITE_ORVANO_ENDPOINT ?? 'http://localhost:7700'

/** Your project's ID, from its overview in the Orvano console. */
export const project: string = import.meta.env.VITE_ORVANO_PROJECT ?? ''
if (project === '') throw new Error('Set VITE_ORVANO_PROJECT in .env.local to your project ID.')
