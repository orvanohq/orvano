import { OrvanoError } from '@orvano/console-client'
import type {
  OAuthProvider,
  OAuthProviderSettings,
  UpdateOAuthProviderRequest,
} from '@orvano/console-client'

/**
 * A write only secret in the form (spec 0012, AC-2, AC-25): kept as stored, replaced by what was
 * typed, or cleared. A secret that isn't set starts as `replace` with an empty input.
 */
export interface SecretValue {
  mode: 'keep' | 'replace' | 'clear'
  value: string
}

/** The provider dialog's values, as typed. */
export interface ProviderValues {
  enabled: boolean
  clientId: string
  clientSecret: SecretValue
  /** One ID per line (Google's native client IDs, Apple's bundle IDs). */
  clientIdsExtra: string
  appleTeamId: string
  appleKeyId: string
  applePrivateKey: SecretValue
  microsoftTenant: string
}

/** The dialog's fields, by the names the API puts before the colon of a refusal. */
export type ProviderField =
  | 'enabled'
  | 'clientId'
  | 'clientSecret'
  | 'clientIdsExtra'
  | 'appleTeamId'
  | 'appleKeyId'
  | 'applePrivateKey'
  | 'microsoftTenant'

const fieldNames: readonly ProviderField[] = [
  'enabled',
  'clientId',
  'clientSecret',
  'clientIdsExtra',
  'appleTeamId',
  'appleKeyId',
  'applePrivateKey',
  'microsoftTenant',
]

/** Which fields each provider has (AC-1). */
export function fieldsOf(provider: OAuthProvider): {
  secret: boolean
  extra: boolean
  apple: boolean
  tenant: boolean
} {
  return {
    secret: provider !== 'apple',
    extra: provider === 'google' || provider === 'apple',
    apple: provider === 'apple',
    tenant: provider === 'microsoft',
  }
}

function secretStart(set: boolean): SecretValue {
  return { mode: set ? 'keep' : 'replace', value: '' }
}

/** The form's starting values from the stored settings; secrets are never there, only whether they are set. */
export function initialValues(settings: OAuthProviderSettings): ProviderValues {
  return {
    enabled: settings.enabled,
    clientId: settings.clientId ?? '',
    clientSecret: secretStart(settings.clientSecretSet),
    clientIdsExtra: settings.clientIdsExtra.join('\n'),
    appleTeamId: settings.appleTeamId ?? '',
    appleKeyId: settings.appleKeyId ?? '',
    applePrivateKey: secretStart(settings.applePrivateKeySet),
    microsoftTenant: settings.microsoftTenant ?? '',
  }
}

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())

/**
 * The whole update the dialog saves: every field the provider has, empty ones as null, and each
 * secret left out (kept), null (cleared), or the new value. An empty Replace keeps the secret.
 */
export function toRequest(
  provider: OAuthProvider,
  values: ProviderValues,
): UpdateOAuthProviderRequest {
  const has = fieldsOf(provider)
  const request: UpdateOAuthProviderRequest = {
    enabled: values.enabled,
    clientId: orNull(values.clientId),
  }
  const secret = secretChange(values.clientSecret)
  if (has.secret && secret !== undefined) request.clientSecret = secret
  if (has.extra) {
    request.clientIdsExtra = values.clientIdsExtra
      .split(/[\n,]/)
      .map((id) => id.trim())
      .filter((id) => id !== '')
  }
  if (has.apple) {
    request.appleTeamId = orNull(values.appleTeamId)
    request.appleKeyId = orNull(values.appleKeyId)
    const key = secretChange(values.applePrivateKey)
    if (key !== undefined) request.applePrivateKey = key
  }
  if (has.tenant) request.microsoftTenant = orNull(values.microsoftTenant)
  return request
}

function secretChange(secret: SecretValue): string | null | undefined {
  if (secret.mode === 'clear') return null
  if (secret.mode === 'replace' && secret.value.trim() !== '') return secret.value.trim()
  return undefined
}

/**
 * Where a refusal goes: the server names the field before a colon (`clientId: Enter ...`); null
 * when it names none, so the error shows above the buttons.
 */
export function placeError(error: unknown): { field: ProviderField; message: string } | null {
  if (!(error instanceof OrvanoError) || error.status !== 400) return null
  const colon = error.message.indexOf(': ')
  if (colon <= 0) return null
  const field = fieldNames.find((name) => name === error.message.slice(0, colon))
  return field === undefined ? null : { field, message: error.message.slice(colon + 2) }
}
