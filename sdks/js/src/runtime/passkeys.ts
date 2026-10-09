import type {
  PasskeyAssertionCredential,
  PasskeyCreationOptions,
  PasskeyRegistrationCredential,
  PasskeyRequestOptions,
} from '../generated/models.js'

/** How a passkey ceremony asks the user (spec 0013, AC-36). */
export interface PasskeyGetRequest {
  /** `conditional` waits for the user to pick a passkey from the browser's autofill. */
  mediation?: 'conditional'
  /** Cancels the ceremony, for example when the user leaves the page. */
  signal?: AbortSignal
}

/**
 * Makes and uses passkeys for the client (spec 0013, AC-36, AC-38): the browser's WebAuthn API by
 * default ({@link browserPasskeys}). Pass your own as `ClientConfig.passkeys`, for example a test
 * authenticator, or a native bridge in a hybrid app. It takes and returns WebAuthn's JSON forms.
 */
export interface PasskeyAuthenticator {
  /** Whether passkeys work here; with `autofill`, whether they can be offered through autofill too. */
  isSupported(options?: { autofill?: boolean }): Promise<boolean>
  /** Runs `navigator.credentials.create` with the options and answers the new passkey. */
  create(
    options: PasskeyCreationOptions,
    signal?: AbortSignal,
  ): Promise<PasskeyRegistrationCredential>
  /** Runs `navigator.credentials.get` with the options and answers the passkey's signature. */
  get(
    options: PasskeyRequestOptions,
    request?: PasskeyGetRequest,
  ): Promise<PasskeyAssertionCredential>
}

/** The parts of the browser's `PublicKeyCredential` constructor this file feature detects. */
interface PublicKeyCredentialStatics {
  parseCreationOptionsFromJSON?: (json: unknown) => PublicKeyCredentialCreationOptions
  parseRequestOptionsFromJSON?: (json: unknown) => PublicKeyCredentialRequestOptions
  isConditionalMediationAvailable?: () => Promise<boolean>
}

/** The parts of a returned credential this file reads; `toJSON` is missing in older browsers. */
interface ReturnedCredential {
  id: string
  type: string
  rawId: ArrayBuffer
  authenticatorAttachment?: string | null
  response: AuthenticatorResponse & {
    attestationObject?: ArrayBuffer
    authenticatorData?: ArrayBuffer
    signature?: ArrayBuffer
    userHandle?: ArrayBuffer | null
    getTransports?: () => string[]
  }
  toJSON?: () => unknown
}

/** The JSON form `toJSON()` gives, as far as this file reads it. */
interface CredentialJson {
  id: string
  rawId: string
  type: string
  authenticatorAttachment?: string | null
  response: {
    clientDataJSON: string
    attestationObject?: string
    transports?: string[]
    authenticatorData?: string
    signature?: string
    userHandle?: string | null
  }
}

function statics(): PublicKeyCredentialStatics | undefined {
  return (globalThis as { PublicKeyCredential?: PublicKeyCredentialStatics }).PublicKeyCredential
}

function credentials(): CredentialsContainer | undefined {
  return (globalThis as { navigator?: { credentials?: CredentialsContainer } }).navigator
    ?.credentials
}

/** Unpadded base64url, the encoding of every binary value in WebAuthn's JSON forms. */
export function toBase64Url(bytes: ArrayBuffer | Uint8Array): string {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes)
  let binary = ''
  for (const byte of view) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** Decodes base64url, with or without padding. */
export function fromBase64Url(value: string): Uint8Array<ArrayBuffer> {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/')
  const binary = atob(base64 + '='.repeat((4 - (base64.length % 4)) % 4))
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

function descriptors(
  list: PasskeyCreationOptions['excludeCredentials'],
): PublicKeyCredentialDescriptor[] {
  return list.map((c) => ({
    type: 'public-key',
    id: fromBase64Url(c.id),
    ...(c.transports === undefined ? {} : { transports: c.transports as AuthenticatorTransport[] }),
  }))
}

function creationOptions(options: PasskeyCreationOptions): PublicKeyCredentialCreationOptions {
  const parse = statics()?.parseCreationOptionsFromJSON
  if (typeof parse === 'function') return parse(options)
  return {
    rp: options.rp,
    user: { ...options.user, id: fromBase64Url(options.user.id) },
    challenge: fromBase64Url(options.challenge),
    pubKeyCredParams: options.pubKeyCredParams.map((p) => ({ type: 'public-key', alg: p.alg })),
    timeout: options.timeout,
    excludeCredentials: descriptors(options.excludeCredentials),
    authenticatorSelection: {
      residentKey: options.authenticatorSelection.residentKey as ResidentKeyRequirement,
      requireResidentKey: options.authenticatorSelection.requireResidentKey,
      userVerification: options.authenticatorSelection
        .userVerification as UserVerificationRequirement,
    },
    attestation: options.attestation as AttestationConveyancePreference,
  }
}

function requestOptions(options: PasskeyRequestOptions): PublicKeyCredentialRequestOptions {
  const parse = statics()?.parseRequestOptionsFromJSON
  if (typeof parse === 'function') return parse(options)
  return {
    challenge: fromBase64Url(options.challenge),
    rpId: options.rpId,
    timeout: options.timeout,
    userVerification: options.userVerification as UserVerificationRequirement,
    allowCredentials: descriptors(options.allowCredentials),
  }
}

/** The credential's JSON form: `toJSON()` when the browser has it, else built by hand. */
function json(credential: ReturnedCredential): CredentialJson {
  if (typeof credential.toJSON === 'function') return credential.toJSON() as CredentialJson
  const r = credential.response
  return {
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
    response: {
      clientDataJSON: toBase64Url(r.clientDataJSON),
      ...(r.attestationObject === undefined
        ? {}
        : {
            attestationObject: toBase64Url(r.attestationObject),
            transports: r.getTransports?.() ?? [],
          }),
      ...(r.authenticatorData === undefined
        ? {}
        : { authenticatorData: toBase64Url(r.authenticatorData) }),
      ...(r.signature === undefined ? {} : { signature: toBase64Url(r.signature) }),
      ...(r.userHandle === undefined
        ? {}
        : { userHandle: r.userHandle === null ? null : toBase64Url(r.userHandle) }),
    },
  }
}

function missing(what: string): Error {
  return new TypeError(`Orvano: the passkey ceremony returned no ${what}.`)
}

/**
 * The default {@link PasskeyAuthenticator}: the browser's WebAuthn API, with the native
 * `PublicKeyCredential.parseCreationOptionsFromJSON`, `parseRequestOptionsFromJSON`, and `toJSON()`
 * where they exist and a small base64url fallback where they don't. It sends only the fields the
 * contract names (no extensions).
 */
export const browserPasskeys: PasskeyAuthenticator = {
  async isSupported(options) {
    const pkc = statics()
    if (pkc === undefined || credentials() === undefined) return false
    if (options?.autofill !== true) return true
    return typeof pkc.isConditionalMediationAvailable === 'function'
      ? await pkc.isConditionalMediationAvailable()
      : false
  },

  async create(options, signal) {
    const container = credentials()
    if (container === undefined) throw new TypeError('Orvano: this runtime has no passkeys.')
    const made = (await container.create({
      publicKey: creationOptions(options),
      ...(signal === undefined ? {} : { signal }),
    })) as ReturnedCredential | null
    if (made === null) throw missing('passkey')
    const j = json(made)
    if (j.response.attestationObject === undefined) throw missing('attestation')
    return {
      id: j.id,
      rawId: j.rawId,
      type: j.type,
      response: {
        clientDataJSON: j.response.clientDataJSON,
        attestationObject: j.response.attestationObject,
        ...(j.response.transports === undefined ? {} : { transports: j.response.transports }),
      },
      authenticatorAttachment: j.authenticatorAttachment ?? null,
    }
  },

  async get(options, request) {
    const container = credentials()
    if (container === undefined) throw new TypeError('Orvano: this runtime has no passkeys.')
    const used = (await container.get({
      publicKey: requestOptions(options),
      ...(request?.mediation === undefined ? {} : { mediation: request.mediation }),
      ...(request?.signal === undefined ? {} : { signal: request.signal }),
    })) as ReturnedCredential | null
    if (used === null) throw missing('passkey')
    const j = json(used)
    if (j.response.authenticatorData === undefined || j.response.signature === undefined)
      throw missing('signature')
    return {
      id: j.id,
      rawId: j.rawId,
      type: j.type,
      response: {
        clientDataJSON: j.response.clientDataJSON,
        authenticatorData: j.response.authenticatorData,
        signature: j.response.signature,
        userHandle: j.response.userHandle ?? null,
      },
      authenticatorAttachment: j.authenticatorAttachment ?? null,
    }
  },
}

/** Options for `signInWithPasskey`. */
export interface PasskeySignInOptions {
  /**
   * Offer the passkeys through the browser's autofill (conditional mediation) instead of a
   * dialog: the promise waits until the user picks one in a field with
   * `autocomplete="username webauthn"`. Check `isPasskeySupported({ autofill: true })` first.
   */
  autofill?: boolean
  /** Cancels the ceremony and the calls, for example when the user leaves the page. */
  signal?: AbortSignal
}

/** Options for `registerPasskey`. */
export interface PasskeyRegistrationOptions {
  /** 1 to 64 characters, shown in the user's passkey list. Left out, it is named `Passkey`. */
  name?: string
  /**
   * The user's current password. A user who has one passes it, unless this session passed a
   * second factor within 10 minutes; a user without one leaves it out.
   */
  password?: string
  /** Cancels the ceremony and the calls. */
  signal?: AbortSignal
}
