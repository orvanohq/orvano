import { updateSession } from '@orvano/nextjs/server'
import type { NextRequest } from 'next/server'
import { endpoint, project } from './lib/config'

/** Refreshes the session before its access token runs out, on every page request. */
export function proxy(request: NextRequest) {
  return updateSession(request, { endpoint, project })
}

export const config = {
  matcher: ['/((?!_next/static|_next/image|favicon.ico).*)'],
}
