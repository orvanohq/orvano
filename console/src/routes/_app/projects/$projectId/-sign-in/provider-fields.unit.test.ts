import { OrvanoError } from '@orvano/console-client'
import type { OAuthProviderSettings } from '@orvano/console-client'
import { describe, expect, it } from 'vitest'

import { initialValues, placeError, toRequest } from './provider-fields.ts'

// Spec 0012 AC-1, AC-2, AC-25: what the provider dialog sends, and where a refusal shows.

const google: OAuthProviderSettings = {
  provider: 'google',
  enabled: true,
  clientId: 'web-client',
  clientSecretSet: true,
  clientSecretHint: '1a2b',
  clientIdsExtra: ['ios-client'],
  appleTeamId: null,
  appleKeyId: null,
  applePrivateKeySet: false,
  microsoftTenant: null,
  redirectReady: true,
  nativeReady: true,
  callbackUrl: 'https://orvano.example.com/v1/projects/p/oauth/google/callback',
  updatedAt: '2026-10-01T00:00:00Z',
}

describe('the provider form', () => {
  it('keeps a stored secret by leaving it out, and sends every other field of the provider', () => {
    const values = initialValues(google)
    expect(values.clientSecret).toEqual({ mode: 'keep', value: '' })
    expect(
      toRequest('google', { ...values, clientIdsExtra: 'ios-client\n android-client ,' }),
    ).toEqual({
      enabled: true,
      clientId: 'web-client',
      clientIdsExtra: ['ios-client', 'android-client'],
    })
  })

  it('clears a secret with null, replaces it with what was typed, and keeps it for an empty Replace', () => {
    const values = initialValues(google)
    expect(
      toRequest('google', { ...values, clientSecret: { mode: 'clear', value: '' } }).clientSecret,
    ).toBeNull()
    expect(
      toRequest('google', { ...values, clientSecret: { mode: 'replace', value: ' new-secret ' } })
        .clientSecret,
    ).toBe('new-secret')
    expect(
      'clientSecret' in
        toRequest('google', { ...values, clientSecret: { mode: 'replace', value: '' } }),
    ).toBe(false)
  })

  it('sends only the fields each provider has', () => {
    const apple = toRequest('apple', {
      ...initialValues({ ...google, provider: 'apple', clientSecretSet: false }),
      appleTeamId: 'TEAM123456',
      appleKeyId: '',
      applePrivateKey: { mode: 'replace', value: '-----BEGIN PRIVATE KEY-----' },
    })
    expect(apple).toMatchObject({
      appleTeamId: 'TEAM123456',
      appleKeyId: null,
      applePrivateKey: '-----BEGIN PRIVATE KEY-----',
    })
    expect('clientSecret' in apple).toBe(false)
    const github = toRequest('github', {
      ...initialValues({ ...google, provider: 'github' }),
      microsoftTenant: 'common',
    })
    expect(
      'clientIdsExtra' in github || 'microsoftTenant' in github || 'appleTeamId' in github,
    ).toBe(false)
  })

  it('places a refusal under the field the server names, else nowhere', () => {
    expect(
      placeError(
        new OrvanoError(400, 'invalid_request', 'clientId: Enter 1 to 255 characters.', null),
      ),
    ).toEqual({
      field: 'clientId',
      message: 'Enter 1 to 255 characters.',
    })
    expect(placeError(new OrvanoError(400, 'invalid_request', 'Something else.', null))).toBeNull()
    expect(placeError(new OrvanoError(403, 'forbidden', 'clientId: no', null))).toBeNull()
  })
})
