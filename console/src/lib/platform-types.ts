import { Globe, Laptop, Monitor, Smartphone, type LucideIcon } from 'lucide-react'

import type { PlatformType } from '@orvano/console-client'

/** How the console labels a platform type and asks for its identifier (spec 0007, Platform form). */
export interface PlatformTypeInfo {
  label: string
  icon: LucideIcon
  identifierLabel: string
  placeholder: string
  hint: string
  /** Spec 0003's identifier rule: the field error for `identifier`, or undefined when it is valid. */
  check: (identifier: string) => string | undefined
}

const appIdMax = 255

function checkPattern(pattern: RegExp, message: string) {
  return (identifier: string): string | undefined => {
    if (identifier === '') return 'Enter an identifier.'
    if (identifier.length > appIdMax) return `Use at most ${String(appIdMax)} characters.`
    return pattern.test(identifier) ? undefined : message
  }
}

function checkLabel(identifier: string): string | undefined {
  if (identifier === '') return 'Enter a label.'
  if (identifier.length > appIdMax) return `Use at most ${String(appIdMax)} characters.`
  // No whitespace or control characters (spec 0003, Platform identifiers).
  return /[\s\p{Cc}]/u.test(identifier) ? 'Use no spaces or control characters.' : undefined
}

/**
 * Every platform type. Typed against the generated `PlatformType`, so a type the contract adds fails
 * the build until it has an entry here.
 */
export const platformTypes: Record<PlatformType, PlatformTypeInfo> = {
  web: {
    label: 'Web',
    icon: Globe,
    identifierLabel: 'Hostname',
    placeholder: 'app.example.com',
    hint: 'A host, *. plus a host, localhost, or an IPv4 address. Scheme and port are ignored.',
    check: checkWebIdentifier,
  },
  android: {
    label: 'Android',
    icon: Smartphone,
    identifierLabel: 'Package name',
    placeholder: 'com.example.app',
    hint: 'The applicationId in your Android build.',
    check: checkPattern(
      /^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$/,
      'Use a package name like com.example.app.',
    ),
  },
  ios: {
    label: 'iOS',
    icon: Smartphone,
    identifierLabel: 'Bundle ID',
    placeholder: 'com.example.app',
    hint: 'The bundle identifier in Xcode.',
    check: checkPattern(
      /^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$/,
      'Use a bundle ID like com.example.app.',
    ),
  },
  macos: {
    label: 'macOS',
    icon: Laptop,
    identifierLabel: 'Bundle ID',
    placeholder: 'com.example.app',
    hint: 'The bundle identifier in Xcode.',
    check: checkPattern(
      /^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$/,
      'Use a bundle ID like com.example.app.',
    ),
  },
  windows: {
    label: 'Windows',
    icon: Monitor,
    identifierLabel: 'Label',
    placeholder: 'My desktop app',
    hint: "Any label; Orvano can't verify desktop apps.",
    check: checkLabel,
  },
  linux: {
    label: 'Linux',
    icon: Monitor,
    identifierLabel: 'Label',
    placeholder: 'My desktop app',
    hint: "Any label; Orvano can't verify desktop apps.",
    check: checkLabel,
  },
}

/** The platform types in the order the Type select lists them. */
export const platformTypeOrder: readonly PlatformType[] = [
  'web',
  'android',
  'ios',
  'macos',
  'windows',
  'linux',
]

const hostLabel = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/

function isIPv4(host: string): boolean {
  const parts = host.split('.')
  return (
    parts.length === 4 &&
    parts.every((part) => /^(0|[1-9][0-9]{0,2})$/.test(part) && Number(part) <= 255)
  )
}

function isHostname(host: string): boolean {
  if (host.length === 0 || host.length > 253) return false
  const labels = host.split('.')
  // An all numeric last label means an IP address, which must be a full IPv4 literal instead.
  if (/^[0-9]+$/.test(labels.at(-1) ?? '')) return false
  return labels.every((label) => label.length <= 63 && hostLabel.test(label))
}

/**
 * Spec 0003's web origin rule, as the server's `WebOriginPattern` checks it: a hostname, `*.` plus a
 * hostname of at least two labels, `localhost`, or an IPv4 address; any other `*` is refused.
 */
export function checkWebIdentifier(identifier: string): string | undefined {
  const value = identifier.trim().toLowerCase()
  if (value === '') return 'Enter a hostname.'
  const wildcard = value.startsWith('*.')
  const host = wildcard ? value.slice(2) : value
  if (host.includes('*'))
    return 'A wildcard is allowed only as the first label, as in *.example.com.'
  if (!isHostname(host) && !isIPv4(host)) {
    return 'Use a hostname (app.example.com), *. plus a hostname, localhost, or an IPv4 address.'
  }
  if (wildcard && (isIPv4(host) || host.split('.').length < 2)) {
    return 'A wildcard needs at least two labels after it, as in *.example.com.'
  }
  return undefined
}

/**
 * Reduces a pasted URL to its lowercase hostname (spec 0007, AC-19):
 * `https://App.example.com:3000/login` becomes `app.example.com`. A value that fails to parse is
 * returned as typed, so validation shows its field error.
 */
export function reduceWebIdentifier(value: string): string {
  const typed = value.trim()
  if (typed.includes('://')) {
    try {
      return new URL(typed).hostname.toLowerCase()
    } catch {
      return value
    }
  }
  return (typed.split('/')[0] ?? '').replace(/:\d+$/, '').toLowerCase()
}
