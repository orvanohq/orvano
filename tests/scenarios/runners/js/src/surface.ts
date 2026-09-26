import { Client, MemorySessionStore } from '@orvano/js'
import { Client as ConsoleClient } from '@orvano/console-client'
import { Client as ServerClient } from '@orvano/js/server'
import { ClientSurface } from './generated/client.js'
import { ConsoleSurface } from './generated/console.js'
import { ServerSurface } from './generated/server.js'
import type { Surface } from './interpreter.js'

/** The API key scenario runners send; the server ignores keys until the auth spec (row 8). */
export const testServerKey = 'test-server-key'

/** How one JS runtime builds its SDK objects. */
export interface SurfaceOptions {
  /** The console session from `fixtures.yaml`; without it console steps skip. */
  consoleSession?: string | undefined
  /**
   * True in a browser page: no API key (setting one throws there), and the browser sends the
   * console cookie itself, so the console client needs no token.
   */
  browser?: boolean
}

/** The client, server, and console SDK objects for one endpoint, as each JS runtime builds them. */
export function createSurface(endpoint: string, options: SurfaceOptions = {}): Surface {
  const server =
    options.browser === true
      ? new ServerClient({ endpoint })
      : new ServerClient({ endpoint, apiKey: testServerKey })
  const hasConsole = options.browser === true || options.consoleSession !== undefined
  return {
    client: new ClientSurface(new Client({ endpoint })),
    server: new ServerSurface(server),
    console: hasConsole
      ? new ConsoleSurface(
          new ConsoleClient({
            endpoint,
            session: new MemorySessionStore(options.consoleSession ?? null),
          }),
        )
      : undefined,
  }
}
