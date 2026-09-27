import { Client } from '@orvano/js'
import { Client as ConsoleClient } from '@orvano/console-client'
import { Client as ServerClient } from '@orvano/js/server'
import { ClientSurface } from './generated/client.js'
import { ConsoleSurface } from './generated/console.js'
import { ServerSurface } from './generated/server.js'
import type { Surface } from './interpreter.js'

/** How one JS runtime builds its SDK objects. */
export interface SurfaceOptions {
  /** The console session from `fixtures.yaml`; without it console steps skip. */
  consoleSession?: string | undefined
  /** The fixture project from `fixtures.yaml`, sent as `X-Orvano-Project` by the client and server SDKs. */
  project?: string | undefined
  /** The fixture API key from `fixtures.yaml`, sent by the server SDK outside a browser. */
  apiKey?: string | undefined
  /**
   * True in a browser page: no API key (setting one throws there), and the browser sends the
   * console cookie itself, so the console client needs no token.
   */
  browser?: boolean
}

/** The client, server, and console SDK objects for one endpoint, as each JS runtime builds them. */
export function createSurface(endpoint: string, options: SurfaceOptions = {}): Surface {
  const project = options.project === undefined ? {} : { project: options.project }
  const serverKey = options.browser !== true && options.apiKey !== undefined
  const server =
    serverKey && options.apiKey !== undefined
      ? new ServerClient({ endpoint, ...project, apiKey: options.apiKey })
      : new ServerClient({ endpoint, ...project })
  const hasConsole = options.browser === true || options.consoleSession !== undefined
  return {
    client: new ClientSurface(new Client({ endpoint, ...project })),
    server: new ServerSurface(server),
    serverKey,
    console: hasConsole
      ? new ConsoleSurface(new ConsoleClient({ endpoint, consoleToken: options.consoleSession }))
      : undefined,
  }
}
