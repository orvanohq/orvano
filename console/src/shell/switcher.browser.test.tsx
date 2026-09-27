import { describe, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { Switcher, type SwitcherItem } from '@/shell/switcher'

const items: SwitcherItem[] = Array.from({ length: 5 }, (_, i) => ({
  id: String(i),
  name: `Org ${String(i)}`,
  status: 'active',
}))

function trigger(): Element {
  const element = document.querySelector('[role=combobox]')
  if (element === null) throw new Error('no combobox')
  return element
}

describe('Switcher paging (AC-13)', () => {
  it('asks for the next page when the popup opens and the end of the list is in view', async () => {
    const fetchNextPage = vi.fn()
    await render(
      <Switcher
        label="Switch org"
        placeholder="Select org"
        current={undefined}
        items={items}
        hasNextPage
        isFetchingNextPage={false}
        fetchNextPage={fetchNextPage}
        onPick={() => undefined}
      />,
    )
    // Closed: nothing is observed yet, so no page is requested.
    expect(fetchNextPage).not.toHaveBeenCalled()
    await userEvent.click(trigger())
    await expect.poll(() => fetchNextPage.mock.calls.length).toBeGreaterThan(0)
  })

  it('does not ask when there is no next page', async () => {
    const fetchNextPage = vi.fn()
    await render(
      <Switcher
        label="Switch org"
        placeholder="Select org"
        current={undefined}
        items={items}
        hasNextPage={false}
        isFetchingNextPage={false}
        fetchNextPage={fetchNextPage}
        onPick={() => undefined}
      />,
    )
    await userEvent.click(trigger())
    await new Promise((resolve) => setTimeout(resolve, 300))
    expect(fetchNextPage).not.toHaveBeenCalled()
  })
})
