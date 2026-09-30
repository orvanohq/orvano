import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { page } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { Button } from '@/components/ui/button'
import { CodeBlock } from '@/components/ui/code-block'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { FormAlert } from '@/components/ui/form-alert'

// An API key or invite link is one long unbroken string. On a phone it must scroll inside its
// block, not stretch the dialog's column past the dialog's edge.

const secret = `orv_sk_${'a1B2c3D4e5'.repeat(8)}`

beforeEach(async () => {
  await page.viewport(360, 740)
})

afterEach(async () => {
  await page.viewport(1280, 800)
})

describe('DialogContent on a phone', () => {
  it('keeps every row inside the dialog when it holds a long key', async () => {
    await render(
      <Dialog open>
        <DialogContent showCloseButton={false} className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Copy your API key</DialogTitle>
            <DialogDescription>Your server sends this key on every call.</DialogDescription>
          </DialogHeader>
          <FormAlert variant="warning" title="This key won't be shown again">
            Store it somewhere safe now.
          </FormAlert>
          <div className="flex flex-col gap-1.5">
            <span className="text-sm font-medium">API key</span>
            <CodeBlock code={secret} label="API key" />
          </div>
          <DialogFooter>
            <Button>Done</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>,
    )

    const dialog = document.querySelector<HTMLElement>('[data-slot=dialog-content]')
    if (dialog === null) throw new Error('no dialog')
    const box = dialog.getBoundingClientRect()
    expect(box.right).toBeLessThanOrEqual(360)
    for (const row of Array.from(dialog.children)) {
      expect(row.getBoundingClientRect().right).toBeLessThanOrEqual(box.right)
    }

    // The key scrolls inside its own block instead.
    const pre = dialog.querySelector('pre')
    expect(pre?.scrollWidth).toBeGreaterThan(pre?.clientWidth ?? 0)
  })
})
