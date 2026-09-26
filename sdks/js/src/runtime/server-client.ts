import { apiKeyHeader, assertNotInBrowser } from './api-key.js'
import { Client as BaseClient } from './client.js'
import type { ClientConfig } from './client.js'

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
 * key; it can also act as a user by carrying a session.
 */
export class Client extends BaseClient {
  #apiKey: string | undefined

  constructor(config: ServerClientConfig) {
    super(config)
    if (config.apiKey !== undefined) this.setKey(config.apiKey)
  }

  /** Sets the API key sent with every call, or removes it with null. Throws in a browser. */
  setKey(apiKey: string | null): void {
    assertNotInBrowser()
    this.#apiKey = apiKey ?? undefined
  }

  protected override async authorize(headers: Headers): Promise<void> {
    await super.authorize(headers)
    if (this.#apiKey !== undefined) headers.set(apiKeyHeader, this.#apiKey)
  }
}
