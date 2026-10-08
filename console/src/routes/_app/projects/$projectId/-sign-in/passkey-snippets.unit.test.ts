import { describe, expect, it } from 'vitest'

import type { Platform } from '@orvano/console-client'

import { androidAssetLinks, appleSiteAssociation, parseFingerprints } from './passkey-snippets.ts'

// Spec 0013 AC-43: the Passkeys card's copyable apple-app-site-association and assetlinks.json.

const at = '2026-10-08T00:00:00Z'
const platform = (type: Platform['type'], identifier: string): Platform => ({
  id: `${type}-${identifier}`,
  type,
  name: identifier,
  identifier,
  createdAt: at,
  updatedAt: at,
})

describe('passkey snippets', () => {
  it('lists each iOS and macOS bundle ID once behind the Team ID placeholder', () => {
    const platforms = [
      platform('ios', 'com.acme.shop'),
      platform('macos', 'com.acme.shop'),
      platform('macos', 'com.acme.desk'),
      platform('web', 'shop.example.com'),
    ]

    expect(JSON.parse(appleSiteAssociation(platforms) ?? 'null')).toEqual({
      webcredentials: { apps: ['<TeamID>.com.acme.shop', '<TeamID>.com.acme.desk'] },
    })
    expect(appleSiteAssociation([platform('web', 'shop.example.com')])).toBeNull()
  })

  it('makes one get_login_creds statement per Android package with every fingerprint', () => {
    const fingerprints = ['AA:BB', 'CC:DD']

    expect(
      JSON.parse(androidAssetLinks([platform('android', 'com.acme.shop')], fingerprints) ?? 'null'),
    ).toEqual([
      {
        relation: ['delegate_permission/common.get_login_creds'],
        target: {
          namespace: 'android_app',
          package_name: 'com.acme.shop',
          sha256_cert_fingerprints: fingerprints,
        },
      },
    ])
    expect(androidAssetLinks([platform('ios', 'com.acme.shop')], fingerprints)).toBeNull()
  })

  it('reads fingerprints one per line or separated by commas', () => {
    expect(parseFingerprints(' AA:BB\n\nCC:DD, EE:FF \n')).toEqual(['AA:BB', 'CC:DD', 'EE:FF'])
    expect(parseFingerprints('   ')).toEqual([])
  })
})
