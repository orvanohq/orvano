import type { ColumnDef } from '@tanstack/react-table'
import { describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { DataTable } from '@/components/ui/data-table'

interface Row {
  name: string
  region: string
  created: string
}

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name' },
  { accessorKey: 'region', header: 'Region' },
  { accessorKey: 'created', header: 'Created' },
]

// Cells never wrap, so this row is far wider than the 300 px container it sits in.
const rows: Row[] = [
  {
    name: 'Beta',
    region: 'eu-central-1',
    created: 'September 26, 2026 at 7:15 PM in the local time zone of the person reading',
  },
]

async function renderNarrowTable() {
  await render(
    <div style={{ width: 300 }}>
      <DataTable label="Apps" columns={columns} data={rows} />
    </div>,
  )
  const region = document.querySelector<HTMLElement>('[role=region][aria-label=Apps]')
  if (region === null) throw new Error('the table region is missing')
  return region
}

describe('a wide DataTable scrolls sideways by keyboard (AC-17)', () => {
  it('scrolls inside its labelled, focusable region, not in a hidden inner container', async () => {
    const region = await renderNarrowTable()

    expect(region.tabIndex).toBe(0)
    expect(region.scrollWidth).toBeGreaterThan(region.clientWidth)
  })

  it('moves the table sideways when the region has focus and the arrow key is pressed', async () => {
    const region = await renderNarrowTable()
    region.focus()
    expect(document.activeElement).toBe(region)
    expect(region.scrollLeft).toBe(0)

    await userEvent.keyboard('{ArrowRight}')

    await expect.poll(() => region.scrollLeft).toBeGreaterThan(0)
  })

  it('leaves the page itself without a horizontal scrollbar', async () => {
    await renderNarrowTable()

    expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(window.innerWidth)
  })
})
