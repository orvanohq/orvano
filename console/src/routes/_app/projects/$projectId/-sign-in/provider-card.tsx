import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { ProviderMark, providerInfo } from '@/auth/providers'
import type { OAuthProviderSettings } from '@orvano/console-client'

/**
 * One provider on the Sign in methods page (spec 0012, AC-25): its state as a word (On or Off) and
 * which ways are ready (Redirect, Native), with a button that opens its settings.
 */
export function ProviderCard({
  settings,
  onOpen,
}: {
  settings: OAuthProviderSettings
  onOpen: () => void
}) {
  const info = providerInfo(settings.provider)
  const ready = [
    settings.redirectReady ? 'Redirect' : null,
    settings.nativeReady ? 'Native' : null,
  ].filter((way): way is string => way !== null)
  const configured = settings.clientId !== null || settings.updatedAt !== null
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ProviderMark provider={settings.provider} />
          <span aria-hidden>{info.label}</span>
        </CardTitle>
        <CardDescription>
          {ready.length === 0 ? 'Not set up' : `Ready: ${ready.join(', ')}`}
        </CardDescription>
        <CardAction>
          <Badge variant="status" tone={settings.enabled ? 'success' : 'neutral'}>
            {settings.enabled ? 'On' : 'Off'}
          </Badge>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-wrap items-center gap-2">
        {ready.map((way) => (
          <Badge key={way} variant="neutral">
            {way}
          </Badge>
        ))}
        <Button
          variant="outline"
          size="sm"
          className="ml-auto"
          aria-label={`${configured ? 'Edit' : 'Set up'} ${info.label}`}
          onClick={onOpen}
        >
          {configured ? 'Edit' : 'Set up'}
        </Button>
      </CardContent>
    </Card>
  )
}
