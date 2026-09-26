import { describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { Button } from '@/components/ui/button'
import { TooltipProvider } from '@/components/ui/tooltip'
import { RoleGate, roleReason } from '@/lib/roles'
import { meetsRole } from '@/shell/nav'
import type { OrgRole } from '@orvano/console-client'

function Panel({ role }: { role: OrgRole }) {
  return (
    <TooltipProvider>
      <RoleGate minRole="owner" role={role}>
        <section aria-label="Danger zone">Owners only area</section>
      </RoleGate>
      <Button
        data-testid="delete"
        disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
      >
        Create key
      </Button>
      <Button
        data-testid="rename"
        disabledReason={meetsRole(role, 'owner') ? undefined : roleReason('owner')}
      >
        Rename org
      </Button>
    </TooltipProvider>
  )
}

describe('role helpers (AC-22)', () => {
  it('hides an owner only area from developers and viewers, and shows it to owners', async () => {
    for (const role of ['viewer', 'developer'] as const) {
      const screen = await render(<Panel role={role} />)
      expect(document.querySelector('[aria-label="Danger zone"]')).toBeNull()
      await screen.unmount()
    }
    const screen = await render(<Panel role="owner" />)
    expect(document.querySelector('[aria-label="Danger zone"]')).not.toBeNull()
    await screen.unmount()
  })

  it('renders an action your role cannot take as disabled but focusable, with the reason described', async () => {
    await render(<Panel role="developer" />)
    const rename = document.querySelector<HTMLElement>('[data-testid=rename]')
    if (rename === null) throw new Error('no rename button')
    await userEvent.tab()
    await userEvent.tab()
    expect(document.activeElement).toBe(rename)
    expect(rename.getAttribute('aria-disabled')).toBe('true')
    const reasonId = rename.getAttribute('aria-describedby')
    expect(document.getElementById(reasonId ?? '')?.textContent).toBe('Owners only')
    // A developer can create keys; that button is a plain, enabled button.
    expect(document.querySelector('[data-testid=delete]')?.getAttribute('aria-disabled')).toBeNull()
  })
})
