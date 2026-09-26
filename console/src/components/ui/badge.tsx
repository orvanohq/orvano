import { cva, type VariantProps } from 'class-variance-authority'
import type * as React from 'react'

import { cn } from '@/lib/utils'

const badgeVariants = cva(
  'inline-flex w-fit shrink-0 items-center gap-1 rounded-sm border border-transparent px-1.5 py-0.5 text-small font-medium whitespace-nowrap',
  {
    variants: {
      variant: {
        neutral: 'bg-secondary text-secondary-foreground',
        primary: 'bg-primary text-primary-foreground',
        success: 'bg-success text-success-foreground',
        warning: 'bg-warning text-warning-foreground',
        destructive: 'bg-destructive text-destructive-foreground',
        // A dot and the word, on a quiet fill; the word carries the meaning, never the color alone.
        status: 'border-border bg-transparent text-foreground',
      },
    },
    defaultVariants: { variant: 'neutral' },
  },
)

type StatusTone = 'success' | 'warning' | 'danger' | 'neutral'

const dotTone: Record<StatusTone, string> = {
  success: 'bg-success-text',
  warning: 'bg-warning-text',
  danger: 'bg-danger-text',
  neutral: 'bg-muted-foreground',
}

interface BadgeProps extends React.ComponentProps<'span'>, VariantProps<typeof badgeVariants> {
  /** With the `status` variant: the color of the leading dot. */
  tone?: StatusTone
}

/** A small label. Not interactive. */
function Badge({ className, variant, tone = 'neutral', children, ...props }: BadgeProps) {
  return (
    <span data-slot="badge" className={cn(badgeVariants({ variant }), className)} {...props}>
      {variant === 'status' ? (
        <span aria-hidden className={cn('size-1.5 rounded-full', dotTone[tone])} />
      ) : null}
      {children}
    </span>
  )
}

export { Badge, badgeVariants }
