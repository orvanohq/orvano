import { sdkVersion } from '../generated/version.js'

/** The SDK's name, sent with its version as `X-Orvano-SDK: @orvano/js/<version>`. */
export const sdkName = '@orvano/js'

/** The header every request carries: this SDK's name and version. */
export const sdkHeader = 'X-Orvano-SDK'

/** The header every response carries: the server's Orvano version. */
export const serverVersionHeader = 'X-Orvano-Version'

/** Where the client sends warnings. `console` satisfies it, and is the default. */
export interface Logger {
  /** Writes one warning. */
  warn(message: string): void
}

/**
 * The warning for a server whose major.minor differs from this SDK's, or null when they match or
 * the server sent no version. SDK `0.4.x` targets Orvano `0.4`.
 */
export function versionMismatch(serverVersion: string | null, endpoint: string): string | null {
  if (serverVersion === null || serverVersion === '') return null
  if (majorMinor(serverVersion) === majorMinor(sdkVersion)) return null
  return (
    `${sdkName} ${sdkVersion} targets Orvano ${majorMinor(sdkVersion)}, but the server at ` +
    `${endpoint} runs ${serverVersion}. Calls still work; use ${sdkName} ` +
    `${majorMinor(serverVersion)}.x to match.`
  )
}

function majorMinor(version: string): string {
  return version.split('.').slice(0, 2).join('.')
}
