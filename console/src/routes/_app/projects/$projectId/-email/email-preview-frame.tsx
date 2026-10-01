import { useEffect, useRef, useState } from 'react'

import { FormAlert } from '@/components/ui/form-alert'

/** The frame page, static in `public/frames/`, served with its own policy (spec 0009, AC-32). */
export const emailPreviewSrc = '/frames/email-preview.html'

/** How long the frame page has to say it is ready before the editor gives up (AC-33). */
export const emailPreviewReadyTimeoutMs = 5000

const readyType = 'orvano.email-preview.ready'
const renderType = 'orvano.email-preview.render'

const isReady = (data: unknown): boolean =>
  typeof data === 'object' && data !== null && 'type' in data && data.type === readyType

/**
 * The email's HTML in two nested frames (spec 0009, AC-31, AC-33). The outer frame loads the frame
 * page in an opaque origin, so nothing in it can reach the console; the page draws the HTML in an
 * inner frame that runs no script. The page says when it is ready, and gets the HTML by
 * `postMessage`: the latest on every ready (a reload included), then each time the HTML changes.
 * Target origin `"*"` is the only one an opaque origin answers to, and the message goes to this
 * frame's own window, whose page can't be swapped from inside.
 */
export function EmailPreviewFrame({ html, className }: { html: string; className?: string }) {
  const frame = useRef<HTMLIFrameElement>(null)
  // What to send on the next ready, and what the page has: null until it first said ready.
  const latest = useRef(html)
  const posted = useRef<string | null>(null)
  const [state, setState] = useState<'waiting' | 'ready' | 'failed'>('waiting')

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const target = frame.current?.contentWindow
      if (target === null || target === undefined || event.source !== target) return
      if (!isReady(event.data)) return
      target.postMessage({ type: renderType, html: latest.current }, '*')
      posted.current = latest.current
      setState('ready')
    }
    window.addEventListener('message', onMessage)
    const timer = window.setTimeout(() => {
      setState((current) => (current === 'waiting' ? 'failed' : current))
    }, emailPreviewReadyTimeoutMs)
    return () => {
      window.removeEventListener('message', onMessage)
      window.clearTimeout(timer)
    }
  }, [])

  useEffect(() => {
    latest.current = html
    const target = frame.current?.contentWindow
    if (
      posted.current === null ||
      posted.current === html ||
      target === null ||
      target === undefined
    )
      return
    target.postMessage({ type: renderType, html }, '*')
    posted.current = html
  }, [html])

  return (
    <div className="flex flex-col gap-3">
      {state === 'failed' ? (
        <FormAlert title="Couldn't load the preview">Reload the page to try again.</FormAlert>
      ) : null}
      {/* Scripts for the frame page only: without allow-same-origin it has an origin of its own. */}
      <iframe
        ref={frame}
        title="Email preview"
        src={emailPreviewSrc}
        sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox"
        className={className}
      />
    </div>
  )
}
