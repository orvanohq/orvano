import { Client, Orvano } from '@orvano/js'
import { Client as ServerClient, Orvano as ServerOrvano } from '@orvano/js/server'
import type { Surface } from './interpreter.js'

/** The client and server SDK objects for one endpoint, as each JS runtime builds them. */
export function createSurface(endpoint: string): Surface {
  return {
    client: new Orvano(new Client({ endpoint })),
    server: new ServerOrvano(new ServerClient({ endpoint })),
  }
}
