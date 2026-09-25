import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'

// Placeholder page until the console shell (scope row 5) lands. It only
// proves the console reaches the API through its own origin.
export const Route = createFileRoute('/')({
  component: Home,
})

interface Health {
  status: string
  version: string
}

// Plain fetch for now; spec 0001 replaces this with @orvano/console-client.
async function fetchHealth(): Promise<Health> {
  const res = await fetch('/v1/health')
  if (!res.ok) throw new Error(`GET /v1/health returned ${String(res.status)}`)
  const body: unknown = await res.json()
  if (!isHealth(body)) throw new Error('GET /v1/health returned an unexpected body')
  return body
}

function isHealth(body: unknown): body is Health {
  return (
    typeof body === 'object' &&
    body !== null &&
    'status' in body &&
    typeof body.status === 'string' &&
    'version' in body &&
    typeof body.version === 'string'
  )
}

function Home() {
  const health = useQuery({ queryKey: ['health'], queryFn: fetchHealth })

  return (
    <main>
      <h1>Orvano</h1>
      <p>The console will live here.</p>
      <p role="status">
        {health.isPending && 'Checking the API…'}
        {health.isError && `API unreachable: ${health.error.message}`}
        {health.isSuccess && `API ${health.data.status}, version ${health.data.version}`}
      </p>
    </main>
  )
}
