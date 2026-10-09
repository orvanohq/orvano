import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import type { MfaFactor, MfaStatus } from '@orvano/console-client'
import { useEffect, useState } from 'react'

import { Skeleton } from '@/components/ui/skeleton'
import { usePageTitle } from '@/lib/page-title'
import { accountMfaQuery, accountPasskeysQuery, keys } from '@/lib/queries'
import { passkeysSupported } from '@/routes/-auth/passkeys'
import { ErrorPanel } from '@/shell/error-panel'
import { PageHeading } from '@/shell/page-heading'

import { PasskeysSection } from './-security/passkeys-section'
import { useStepUp } from './-security/step-up'
import { TotpSection } from './-security/totp-section'

export const Route = createFileRoute('/_app/account/security')({
  component: SecurityPage,
})

/**
 * Your console account's Security page (spec 0013, AC-42): the authenticator app with its recovery
 * codes, and your passkeys. Changes that Orvano guards with step up (turning the app on or off, new
 * codes, adding or removing a passkey) open a dialog when they need a fresh second factor or sign
 * in, then repeat once.
 */
function SecurityPage() {
  usePageTitle('Security')
  const queryClient = useQueryClient()
  const mfa = useQuery(accountMfaQuery())
  const passkeys = useQuery(accountPasskeysQuery())
  const [supported, setSupported] = useState(false)

  useEffect(() => {
    void passkeysSupported().then(setSupported)
  }, [])

  const activePasskeys = mfa.data?.passkeyCount ?? 0
  const stepUp = useStepUp({
    factors: mfa.data === undefined ? [] : proofFactors(mfa.data, supported),
    hasPasskey: supported && activePasskeys > 0,
  })
  const changed = async () => {
    await queryClient.invalidateQueries({ queryKey: keys.account })
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-6">
      <PageHeading>Security</PageHeading>
      {mfa.isError ? (
        <ErrorPanel
          error={mfa.error}
          onRetry={() => {
            void mfa.refetch()
          }}
        />
      ) : mfa.data === undefined ? (
        <div aria-busy className="flex flex-col gap-6">
          <Skeleton aria-hidden className="h-40 w-full" />
          <Skeleton aria-hidden className="h-40 w-full" />
        </div>
      ) : (
        <>
          <TotpSection mfa={mfa.data} run={stepUp.run} onChanged={changed} />
          <PasskeysSection
            passkeys={passkeys.data?.items ?? []}
            loading={passkeys.isPending}
            error={passkeys.isError ? passkeys.error : undefined}
            onRetry={() => {
              void passkeys.refetch()
            }}
            supported={supported}
            run={stepUp.run}
            onChanged={changed}
          />
        </>
      )}
      {stepUp.dialog}
    </div>
  )
}

/** The factors a step up can take from this account: those of its MFA, and a usable passkey. */
function proofFactors(mfa: MfaStatus, browserPasskeys: boolean): MfaFactor[] {
  const factors: MfaFactor[] = []
  if (mfa.mfaEnabled) factors.push('totp')
  if (mfa.mfaEnabled && mfa.recoveryCodesRemaining > 0) factors.push('recovery_code')
  if (browserPasskeys && mfa.passkeyCount > 0) factors.push('passkey')
  return factors
}
