import { readFileSync } from 'node:fs'
import type { IncomingMessage, ServerResponse } from 'node:http'
import { fileURLToPath } from 'node:url'
import type { Plugin } from 'vite'

/** The email preview frame page (spec 0009, AC-31), served from `public/`. */
export const emailPreviewPath = '/frames/email-preview.html'

/** The one file that lists the frame page's headers (spec 0009, AC-32). */
export const emailPreviewHeadersFile = fileURLToPath(
  new URL('../deploy/gateway/email-preview-headers.caddy', import.meta.url),
)

/**
 * Reads `email-preview-headers.caddy`: one `Name "value"` per line, double quotes, no escapes
 * inside, blank lines and `#` comments allowed. Throws, naming the file, on a line it can't read or
 * when there is no `Content-Security-Policy`.
 */
export function readEmailPreviewHeaders(file = emailPreviewHeadersFile): Map<string, string> {
  const headers = new Map<string, string>()
  readFileSync(file, 'utf8')
    .split('\n')
    .forEach((raw, index) => {
      const line = raw.trim()
      if (line === '' || line.startsWith('#')) return
      const match = /^([A-Za-z0-9-]+)\s+"([^"\\]*)"$/.exec(line)
      if (match === null) throw new Error(`${file}:${String(index + 1)}: expected Name "value"`)
      const [, name = '', value = ''] = match
      headers.set(name, value)
    })
  if (!headers.has('Content-Security-Policy'))
    throw new Error(`${file}: no Content-Security-Policy header found`)
  return headers
}

/**
 * Serves the frame page with its production headers in `vite` and `vite preview` (never the build),
 * so dev and the AppHost show the preview as the gateway does (spec 0009, AC-32). The path match is
 * case sensitive, as the gateway's is.
 */
export function emailPreviewHeaders(): Plugin {
  // Read when a server starts, not here: the gateway image builds the console without `deploy/`.
  const middleware = () => {
    const headers = readEmailPreviewHeaders()
    return (request: IncomingMessage, response: ServerResponse, next: () => void) => {
      if (request.url?.split('?')[0] === emailPreviewPath)
        for (const [name, value] of headers) response.setHeader(name, value)
      next()
    }
  }
  return {
    name: 'orvano-email-preview-headers',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use(middleware())
    },
    configurePreviewServer(server) {
      server.middlewares.use(middleware())
    },
  }
}
