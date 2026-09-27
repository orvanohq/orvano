import type { Client as ServerClient } from '@orvano/js/server'
import type { DispatchTable } from './dispatch-table.js'

/**
 * Runner operations: calls the scenarios make that are not contract operations. `signIn` is a
 * plain sign in call that leaves the SDK's stored session alone, so a runner without client
 * operations (.NET) can get a token too; `verifyAccessToken` is the server SDK's own check. Their
 * names have no dot, so they never collide with an operationId.
 */
export const runnerDispatch: DispatchTable = {
  signIn: {
    status: 201,
    client: (o, input) =>
      o.client.request<unknown>({
        method: 'POST',
        path: '/v1/account/sessions/password',
        body: input.body,
      }),
  },
  verifyAccessToken: {
    status: 200,
    server: async (o, input) => {
      const verified = await (o.client as ServerClient).verifyAccessToken(String(input.token), {
        online: input.online === true,
      })
      return {
        userId: verified.userId,
        sessionId: verified.sessionId,
        expiresAt: verified.expiresAt.toISOString(),
      }
    },
  },
}
