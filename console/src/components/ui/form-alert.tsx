import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'

const variants = {
  error: { icon: CircleAlert, tone: 'text-danger-text' },
  warning: { icon: TriangleAlert, tone: 'text-warning-text' },
  info: { icon: Info, tone: 'text-muted-foreground' },
  success: { icon: CircleCheck, tone: 'text-success-text' },
} as const

/**
 * The alert at the top of a form: a server error on submit shows here (AC-21). Errors use
 * `role="alert"` so they are announced at once; the rest are polite status messages.
 */
export function FormAlert({
  variant = 'error',
  title,
  children,
}: {
  variant?: keyof typeof variants
  title: string
  children?: ReactNode
}) {
  const { icon: Icon, tone } = variants[variant]
  return (
    <Alert role={variant === 'error' ? 'alert' : 'status'}>
      <Icon aria-hidden className={tone} />
      <AlertTitle>{title}</AlertTitle>
      {children === undefined ? null : <AlertDescription>{children}</AlertDescription>}
    </Alert>
  )
}
