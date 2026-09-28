import type { ReactNode } from 'react'

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/**
 * One card of a Settings page, under its own `h2` (spec 0007, Layout). The `danger` tone is the
 * Danger zone: a destructive border around the actions that delete things.
 */
export function SettingsSection({
  title,
  description,
  tone = 'default',
  children,
}: {
  title: string
  description?: ReactNode
  tone?: 'default' | 'danger'
  children: ReactNode
}) {
  return (
    <Card className={cn(tone === 'danger' && 'border-destructive')}>
      <CardHeader>
        <CardTitle>
          <h2>{title}</h2>
        </CardTitle>
        {description === undefined ? null : <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent className="flex flex-col gap-4">{children}</CardContent>
    </Card>
  )
}

/** One action in the Danger zone: what it does on the left, its button on the right. */
export function DangerAction({
  title,
  description,
  children,
}: {
  title: string
  description: ReactNode
  children: ReactNode
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="flex max-w-prose flex-col gap-0.5">
        <span className="font-medium">{title}</span>
        <span className="text-muted-foreground">{description}</span>
      </div>
      {children}
    </div>
  )
}
