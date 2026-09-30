import * as React from 'react'
import { cn } from '@/lib/utils'

function Textarea({ className, ...props }: React.ComponentProps<'textarea'>) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        'flex field-sizing-content min-h-16 w-full rounded-md border border-input bg-background px-2.5 py-2 text-field transition-colors outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:bg-background disabled:opacity-50 aria-invalid:border-destructive ',
        className,
      )}
      {...props}
    />
  )
}

export { Textarea }
