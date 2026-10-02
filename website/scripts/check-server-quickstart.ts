/**
 * The CI check for the server quickstarts (spec 0011, AC-12, AC-19): signs in as a user the example's create user
 * command made, exactly like the quickstart's `curl` does, then checks that the running Dart or .NET server answers
 * `GET /me` with 200 and that user for the token, and 401 without a token or with a bad one.
 *
 *   ORVANO_ENDPOINT=... ORVANO_PROJECT=... pnpm --filter @orvano/website check:server-quickstart \
 *     http://localhost:3001 <email> <password>
 */
import process from 'node:process'

const [appUrl, email, password] = process.argv.slice(2)
const endpoint = process.env.ORVANO_ENDPOINT ?? ''
const project = process.env.ORVANO_PROJECT ?? ''
if (
  appUrl === undefined ||
  email === undefined ||
  password === undefined ||
  endpoint === '' ||
  project === ''
) {
  throw new Error(
    'Usage: ORVANO_ENDPOINT=... ORVANO_PROJECT=... check-server-quickstart <server URL> <email> <password>',
  )
}

const signIn = await fetch(`${endpoint}/v1/account/sessions/password`, {
  method: 'POST',
  headers: { 'content-type': 'application/json', 'x-orvano-project': project },
  body: JSON.stringify({ email, password }),
})
if (!signIn.ok) throw new Error(`Sign in answered ${String(signIn.status)}: ${await signIn.text()}`)
const { session } = (await signIn.json()) as { session: { accessToken: string } }

const me = await fetch(`${appUrl}/me`, {
  headers: { authorization: `Bearer ${session.accessToken}` },
})
if (me.status !== 200)
  throw new Error(`GET /me with a token answered ${String(me.status)}: ${await me.text()}`)
const user = (await me.json()) as { id?: unknown; email?: unknown; name?: unknown }
if (typeof user.id !== 'string' || user.email !== email) {
  throw new Error(`GET /me answered with the wrong user: ${JSON.stringify(user)}`)
}

for (const [label, headers] of [
  ['no token', {}],
  ['a bad token', { authorization: 'Bearer not.a.token' }],
] as const) {
  const response = await fetch(`${appUrl}/me`, { headers })
  if (response.status !== 401)
    throw new Error(`GET /me with ${label} answered ${String(response.status)}, not 401.`)
}

console.log(`${appUrl}: /me answered the signed in user, and 401 without a valid token.`)
