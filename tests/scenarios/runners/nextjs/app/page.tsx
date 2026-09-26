import { createServerClient } from '@orvano/nextjs'
import { cookies } from 'next/headers'

export const dynamic = 'force-dynamic'

/** A server component calling Orvano, to prove the helper works there (cookies are read only). */
export default async function Page() {
  const orvano = createServerClient({
    endpoint: process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080',
    cookies: await cookies(),
  })
  const health = await orvano.health.get()
  return <p>Orvano {health.version}</p>
}
