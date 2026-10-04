import type { OAuthProvider } from '@orvano/console-client'

import { cn } from '@/lib/utils'

/** What the console shows for each sign in provider (spec 0012). */
export interface ProviderInfo {
  /** The provider's name, as people know it. */
  label: string
  /** The short mark shown in tight places, beside the name for screen readers. */
  mark: string
  /** The docs page that walks through setting the provider up. */
  docs: string
  /** What the `clientId` field is called at this provider. */
  clientIdLabel: string
}

/** The four providers in the order the console lists them. */
export const providerOrder: readonly OAuthProvider[] = ['google', 'apple', 'github', 'microsoft']

const docsBase = 'https://orvano.dev/docs/auth'

/** Labels, marks, docs pages, and field names per provider. */
export const providers: Record<Exclude<OAuthProvider, 'unknown'>, ProviderInfo> = {
  google: {
    label: 'Google',
    mark: 'G',
    docs: `${docsBase}/sign-in-with-google/`,
    clientIdLabel: 'Web client ID',
  },
  apple: {
    label: 'Apple',
    mark: 'A',
    docs: `${docsBase}/sign-in-with-apple/`,
    clientIdLabel: 'Services ID',
  },
  github: {
    label: 'GitHub',
    mark: 'GH',
    docs: `${docsBase}/sign-in-with-github/`,
    clientIdLabel: 'Client ID',
  },
  microsoft: {
    label: 'Microsoft',
    mark: 'M',
    docs: `${docsBase}/sign-in-with-microsoft/`,
    clientIdLabel: 'Application (client) ID',
  },
}

/** A provider's info; an unknown provider (a newer server) shows its raw name. */
export function providerInfo(provider: string): ProviderInfo {
  const known = providers[provider as keyof typeof providers] as ProviderInfo | undefined
  return (
    known ?? {
      label: provider,
      mark: provider.slice(0, 1).toUpperCase(),
      docs: docsBase,
      clientIdLabel: 'Client ID',
    }
  )
}

/**
 * A provider's mark: a small square with its letters, hidden from screen readers, which hear the
 * provider's name instead (spec 0012, AC-26).
 */
export function ProviderMark({ provider, className }: { provider: string; className?: string }) {
  const info = providerInfo(provider)
  return (
    <span className={cn('inline-flex items-center', className)} title={info.label}>
      <span
        aria-hidden
        className="inline-flex h-5 min-w-5 items-center justify-center rounded-sm border bg-muted px-1 text-xs font-semibold text-foreground"
      >
        {info.mark}
      </span>
      <span className="sr-only">{info.label}</span>
    </span>
  )
}
