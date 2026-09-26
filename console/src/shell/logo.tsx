/** The Orvano ring mark; swapping in a real logo changes this file and `public/favicon.svg`. */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" fill="none" aria-hidden className={className}>
      <circle cx="16" cy="16" r="10" stroke="currentColor" strokeWidth="5" />
    </svg>
  )
}

/** The mark with the "Orvano" wordmark in Inter 600. */
export function Logo() {
  return (
    <span className="inline-flex items-center gap-2 font-semibold">
      <LogoMark className="size-6 text-primary" />
      Orvano
    </span>
  )
}
