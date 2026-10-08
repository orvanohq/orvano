import type { Platform } from '@orvano/console-client'

/** Stands in for the Apple Team ID, which Orvano never stores: the developer replaces it. */
export const teamIdPlaceholder = '<TeamID>'

/**
 * The `/.well-known/apple-app-site-association` file that lets the project's iOS and macOS apps use
 * passkeys of the RP ID (spec 0013, AC-43): one `webcredentials` entry per bundle ID, each behind
 * the Team ID placeholder. Null when the project has no iOS or macOS platform.
 */
export function appleSiteAssociation(platforms: readonly Platform[]): string | null {
  const bundles = unique(
    platforms.filter((p) => p.type === 'ios' || p.type === 'macos').map((p) => p.identifier),
  )
  if (bundles.length === 0) return null
  return JSON.stringify(
    { webcredentials: { apps: bundles.map((id) => `${teamIdPlaceholder}.${id}`) } },
    null,
    2,
  )
}

/**
 * The `/.well-known/assetlinks.json` file that lets the project's Android apps use passkeys of the
 * RP ID (spec 0013, AC-43): one statement per package name with every fingerprint. Null when the
 * project has no Android platform.
 */
export function androidAssetLinks(
  platforms: readonly Platform[],
  fingerprints: readonly string[],
): string | null {
  const packages = unique(platforms.filter((p) => p.type === 'android').map((p) => p.identifier))
  if (packages.length === 0) return null
  return JSON.stringify(
    packages.map((name) => ({
      relation: ['delegate_permission/common.get_login_creds'],
      target: {
        namespace: 'android_app',
        package_name: name,
        sha256_cert_fingerprints: fingerprints,
      },
    })),
    null,
    2,
  )
}

/** The fingerprints typed one per line (or separated by commas or spaces), without blanks. */
export function parseFingerprints(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((part) => part.trim())
    .filter((part) => part !== '')
}

function unique(values: readonly string[]): string[] {
  return [...new Set(values)]
}
