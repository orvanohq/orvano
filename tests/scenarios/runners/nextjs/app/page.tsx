import { createServerClient } from '@orvano/nextjs'

export const dynamic = 'force-dynamic'

/** A server component calling Orvano, to prove the helper works there. */
export default async function Page() {
  const orvano = createServerClient({
    endpoint: process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080',
  })
  const health = await orvano.health.get()
  return <p>Orvano {health.version}</p>
}
