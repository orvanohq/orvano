import { Download } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { CopyButton } from '@/components/ui/copy-button'
import { FormAlert } from '@/components/ui/form-alert'

/**
 * New recovery codes, shown this once (spec 0013, AC-42): each works once when the authenticator
 * app is gone. Copy puts all ten on the clipboard, one per line; Download saves them as a text file.
 * `onDone` hides them for good.
 */
export function RecoveryCodesPanel({
  codes,
  onDone,
}: {
  codes: readonly string[]
  onDone: () => void
}) {
  const text = codes.join('\n')
  return (
    <div className="flex flex-col gap-(--stack)">
      <FormAlert variant="warning" title="Save your recovery codes now">
        They show only this once. Each code works once, if you lose your authenticator app.
      </FormAlert>
      <ul
        aria-label="Recovery codes"
        className="grid grid-cols-2 gap-x-6 gap-y-1 font-mono text-mono"
      >
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2">
        <CopyButton value={text} label="Copy codes" />
        <Button
          variant="outline"
          onClick={() => {
            downloadText('orvano-recovery-codes.txt', `${text}\n`)
          }}
        >
          <Download aria-hidden />
          Download
        </Button>
        <Button onClick={onDone}>I saved them</Button>
      </div>
    </div>
  )
}

/** Saves `text` as a file through a temporary link; nothing leaves the browser. */
function downloadText(name: string, text: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }))
  const link = document.createElement('a')
  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
