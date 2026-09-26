import { Button as ButtonPrimitive } from '@base-ui/react/button'
import { cva, type VariantProps } from 'class-variance-authority'
import { Loader2 } from 'lucide-react'
import { useId } from 'react'

import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'

const buttonVariants = cva(
  "group/button inline-flex shrink-0 items-center justify-center gap-1.5 rounded-md border border-transparent text-body font-medium whitespace-nowrap transition-colors select-none disabled:pointer-events-none disabled:opacity-50 aria-disabled:bg-muted aria-disabled:text-muted-foreground aria-disabled:border-transparent [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-(--icon)",
  {
    variants: {
      variant: {
        primary: 'bg-primary text-primary-foreground hover:bg-primary/90',
        secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/80',
        outline: 'border-input bg-background hover:bg-accent hover:text-accent-foreground',
        ghost: 'hover:bg-accent hover:text-accent-foreground aria-expanded:bg-accent',
        destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
        link: 'text-link underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-(--control-h) px-(--control-px)',
        sm: 'h-(--control-h-sm) px-(--control-px) text-small',
        icon: 'size-(--control-h)',
      },
    },
    defaultVariants: {
      variant: 'primary',
      size: 'default',
    },
  },
)

interface ButtonProps extends ButtonPrimitive.Props, VariantProps<typeof buttonVariants> {
  /** Shows a spinner and sets `aria-busy`; the button stays focusable but does nothing. */
  loading?: boolean
  /**
   * Why this action is unavailable to you (for example "Owners only"). The button stays focusable
   * and does nothing; the reason shows in a tooltip and is read as its description (AC-22).
   */
  disabledReason?: string
}

/**
 * The console button. Loading keeps focus on the button (`focusableWhenDisabled`) and ignores
 * clicks, so a screen reader user is not thrown off it while an action runs.
 */
function Button({
  className,
  variant,
  size,
  loading = false,
  disabled = false,
  focusableWhenDisabled = false,
  disabledReason,
  children,
  ...props
}: ButtonProps) {
  const reasonId = useId()
  const blocked = disabledReason !== undefined
  const button = (
    <ButtonPrimitive
      data-slot="button"
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
      aria-busy={loading || undefined}
      aria-describedby={blocked ? reasonId : props['aria-describedby']}
      focusableWhenDisabled={loading || blocked || focusableWhenDisabled}
      disabled={loading || blocked || disabled}
    >
      {loading ? <Loader2 className="animate-spin" aria-hidden /> : null}
      {children}
    </ButtonPrimitive>
  )
  if (!blocked) return button
  return (
    <>
      <Tooltip>
        <TooltipTrigger render={button} />
        <TooltipContent>{disabledReason}</TooltipContent>
      </Tooltip>
      <span id={reasonId} className="sr-only">
        {disabledReason}
      </span>
    </>
  )
}

export { Button, buttonVariants }
