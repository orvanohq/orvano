import { CopyButton } from '@/components/ui/copy-button'
import { cn } from '@/lib/utils'

/** Inline code, in mono. */
export function InlineCode({ className, ...props }: React.ComponentProps<'code'>) {
  return <code className={cn('rounded-sm bg-muted px-1 py-0.5 font-mono', className)} {...props} />
}

/** A block of code with a copy button. No syntax colors yet; the scroll container is focusable. */
export function CodeBlock({ code, label }: { code: string; label: string }) {
  return (
    <div className="relative rounded-lg border border-border bg-card">
      <pre tabIndex={0} aria-label={label} className="overflow-auto p-3 pr-12 font-mono text-mono">
        <code>{code}</code>
      </pre>
      <div className="absolute top-1 right-1">
        <CopyButton value={code} label={`Copy ${label}`} />
      </div>
    </div>
  )
}

/** An ID or key in mono with its copy button. */
export function CopyableId({ value, label }: { value: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1">
      <code className="font-mono text-mono">{value}</code>
      <CopyButton value={value} label={`Copy ${label}`} />
    </span>
  )
}
