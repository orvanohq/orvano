import { AccessTokenVerifier } from './access-tokens.js'
import type { VerifiedAccessToken, VerifyAccessTokenOptions } from './access-tokens.js'
import { apiKeyHeader, assertNotInBrowser } from './api-key.js'
import { Client as BaseClient } from './client.js'
import type { ClientConfig } from './client.js'
import { MemorySessionStore } from './auth.js'
import type { AuthSession } from './auth.js'

/** Settings for the server {@link Client}. */
export interface ServerClientConfig extends ClientConfig {
  /**
   * An API key, sent with every call. It grants admin power, so keep it in server code: setting
   * one in a browser throws.
   */
  apiKey?: string
}

/**
 * Sends requests to one Orvano server from trusted server code. Like the app client, plus an API
 * key and a local check of users' access tokens.
 */
export class Client extends BaseClient {
  #apiKey: string | undefined
  readonly #verifier = new AccessTokenVerifier(this)

  constructor(config: ServerClientConfig) {
    // Servers never share a browser's stored session: memory unless told otherwise.
    super({ ...config, session: config.session ?? new MemorySessionStore() })
    if (config.apiKey !== undefined) this.setKey(config.apiKey)
  }

  /** Sets the API key sent with every call, or removes it with null. Throws in a browser. */
  setKey(apiKey: string | null): void {
    assertNotInBrowser()
    this.#apiKey = apiKey ?? undefined
  }

  /**
   * Checks a user's access token without calling Orvano: an ES256 signature by one of the
   * project's keys (fetched from its JWKS and kept for 10 minutes), issued by this endpoint for
   * this project, and not expired (30 seconds leeway). A token whose session ended still passes
   * until it expires (at most 15 minutes); pass `online: true` to also ask Orvano, as the user and
   * never with the API key, whether the session is still active. The answer carries `aal` and
   * `amr`, and `requireMfa: true` refuses a session that signed in with one factor.
   *
   * @throws {@link OrvanoError} with status 401 and code `token_expired` or `invalid_token` when the
   * token does not check out, 403 `mfa_required` when `requireMfa` is set and `aal` is below 2, and
   * `Error` when the client has no project.
   */
  verifyAccessToken(
    token: string,
    options?: VerifyAccessTokenOptions,
  ): Promise<VerifiedAccessToken> {
    return this.#verifier.verify(token, options)
  }

  protected override async authorize(headers: Headers, session: AuthSession | null): Promise<void> {
    await super.authorize(headers, session)
    if (this.#apiKey !== undefined) headers.set(apiKeyHeader, this.#apiKey)
  }
}
