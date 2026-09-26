import { Client, Orvano } from '@orvano/console-client'

// Same origin as the API: the Vite proxy in dev, Caddy in production (spec 0002), so the console
// cookie travels without CORS. Every console call goes through these clients, never `fetch`.
const orgClient = new Orvano(new Client({ endpoint: window.location.origin }))

const projectClients = new Map<string, Orvano>()

/** The client for calls that need no project: orgs and their project lists. */
export function consoleApi(): Orvano {
  return orgClient
}

/** The client bound to one project (it sends `X-Orvano-Project`), created once per project. */
export function projectClient(projectId: string): Orvano {
  let client = projectClients.get(projectId)
  if (client === undefined) {
    client = new Orvano(new Client({ endpoint: window.location.origin, project: projectId }))
    projectClients.set(projectId, client)
  }
  return client
}
