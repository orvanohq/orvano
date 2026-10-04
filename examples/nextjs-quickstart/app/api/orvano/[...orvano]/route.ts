// #region route-handler
import { createOrvanoRouteHandler } from '@orvano/nextjs/server'
import { endpoint, project } from '@/lib/config'

/**
 * Orvano's route handler: it starts provider sign in (`/api/orvano/oauth`), finishes it when the
 * provider sends the browser back (`/api/orvano/oauth-callback`), and sets the session cookies.
 */
export const { GET, POST } = createOrvanoRouteHandler({ endpoint, project })
// #endregion route-handler
