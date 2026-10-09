import type { PasskeyAuthenticator } from '@orvano/js'
import type { TestService } from './generated/client.js'

/**
 * The origin the software authenticator names in its client data: a web platform of the scenario
 * project (`localhost`, any port), whatever page or process the runner lives in.
 */
export const passkeyOrigin = 'http://localhost:3000'

/**
 * A {@link PasskeyAuthenticator} on the server's `Test` only software authenticator (spec 0013,
 * AC-45): `create` asks `test.createPasskeyCredential` for a new passkey, and `get` signs with the
 * newest one the options allow (any, for a sign in with an empty `allowCredentials`). It holds only
 * the credential IDs; the keys live in the server for the run.
 */
export function testPasskeys(test: () => TestService): PasskeyAuthenticator {
  const made: string[] = []
  return {
    isSupported: () => Promise.resolve(true),
    async create(options) {
      const credential = await test().createPasskeyCredential({ options, origin: passkeyOrigin })
      made.push(credential.id)
      return credential
    },
    async get(options) {
      const allowed = new Set(options.allowCredentials.map((c) => c.id))
      const id = [...made].reverse().find((m) => allowed.size === 0 || allowed.has(m))
      if (id === undefined)
        throw new Error('the test authenticator made no passkey these options allow')
      return test().createPasskeyAssertion({ options, origin: passkeyOrigin, credentialId: id })
    },
  }
}
