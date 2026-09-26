import { Check, Copy } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'

type CopyState = 'idle' | 'copied' | 'failed'

/**
 * Copies `value` to the clipboard. After a copy it shows a check for 2 seconds and announces
 * "Copied" through a polite live region; a blocked clipboard announces "Copy failed".
 */
export function CopyButton({ value, label = 'Copy' }: { value: string; label?: string }) {
  const [state, setState] = useState<CopyState>('idle')
  const timer = useRef<number | undefined>(undefined)

  useEffect(
    () => () => {
      window.clearTimeout(timer.current)
    },
    [],
  )

  const copy = () => {
    void navigator.clipboard
      .writeText(value)
      .then(() => {
        setState('copied')
      })
      .catch(() => {
        setState('failed')
      })
      .finally(() => {
        window.clearTimeout(timer.current)
        timer.current = window.setTimeout(() => {
          setState('idle')
        }, 2000)
      })
  }

  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        aria-label={label}
        onClick={copy}
        className="size-(--control-h-sm)"
      >
        {state === 'copied' ? <Check aria-hidden /> : <Copy aria-hidden />}
      </Button>
      <span role="status" aria-live="polite" className="sr-only">
        {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : ''}
      </span>
    </>
  )
}
