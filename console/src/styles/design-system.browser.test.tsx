/// <reference types="@vitest/browser-playwright" />
// (Types `cdp()` as Playwright's CDP session, for the touch screen tests.)
import { afterEach, describe, expect, it } from 'vitest'
import { cdp, userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'
import { ChevronRight } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'

function setDensity(density: 'compact' | 'comfortable') {
  document.documentElement.dataset.density = density
}

afterEach(() => {
  setDensity('compact')
})

function Sample() {
  return (
    <main>
      <Button data-testid="button">Save</Button>
      <p data-testid="body">Body text</p>
      <Button size="icon" aria-label="Next" data-testid="icon-button">
        <ChevronRight data-testid="icon" aria-hidden />
      </Button>
      <DataTable
        label="Rows"
        columns={[{ accessorKey: 'name', header: 'Name' }]}
        data={[{ name: 'One' }]}
      />
    </main>
  )
}

const rowHeight = () => document.querySelector('tbody tr')?.getBoundingClientRect().height

const size = (testId: string, property: 'height' | 'fontSize' | 'width') =>
  Number.parseFloat(
    getComputedStyle(document.querySelector(`[data-testid=${testId}]`) ?? document.body)[property],
  )

describe('density (AC-3)', () => {
  it('is compact by default: 32 px controls, 14 px body, 16 px icons, 36 px rows', async () => {
    await render(<Sample />)
    expect(size('button', 'height')).toBe(32)
    expect(size('body', 'fontSize')).toBe(14)
    expect(size('icon', 'width')).toBe(16)
    expect(rowHeight()).toBe(36)
  })

  it('is comfortable on request: 40 px controls, 16 px body, 18 px icons, 44 px rows', async () => {
    setDensity('comfortable')
    await render(<Sample />)
    expect(size('button', 'height')).toBe(40)
    expect(size('body', 'fontSize')).toBe(16)
    expect(size('icon', 'width')).toBe(18)
    expect(rowHeight()).toBe(44)
  })
})

describe('field text on touch screens', () => {
  // DevTools' touch emulation (as in device mode) is what makes `(pointer: coarse)` match.
  const emulatePointer = (value: 'coarse' | 'fine') =>
    cdp().send('Emulation.setTouchEmulationEnabled', {
      enabled: value === 'coarse',
      maxTouchPoints: 5,
    })

  afterEach(async () => {
    await emulatePointer('fine')
  })

  function Fields() {
    return (
      <main>
        <Input aria-label="Email" data-testid="input" />
        <Textarea aria-label="Notes" data-testid="textarea" />
      </main>
    )
  }

  it('is 16 px in compact density, so iOS does not zoom into the field', async () => {
    await emulatePointer('coarse')
    expect(matchMedia('(pointer: coarse)').matches).toBe(true)
    await render(<Fields />)
    expect(size('input', 'fontSize')).toBe(16)
    expect(size('textarea', 'fontSize')).toBe(16)
    expect(size('input', 'height')).toBe(32)
  })

  it('follows the body role with a mouse: 14 px compact', async () => {
    expect(matchMedia('(pointer: coarse)').matches).toBe(false)
    await render(<Fields />)
    expect(size('input', 'fontSize')).toBe(14)
    expect(size('textarea', 'fontSize')).toBe(14)
  })
})

describe('focus ring (AC-6)', () => {
  it('shows a solid 2 px ring on keyboard focus', async () => {
    await render(<Sample />)
    const button = document.querySelector<HTMLElement>('[data-testid=button]')
    if (button === null) throw new Error('no button')

    await userEvent.tab()
    expect(document.activeElement).toBe(button)
    const ring = getComputedStyle(button)
    expect(ring.outlineStyle).toBe('solid')
    expect(ring.outlineWidth).toBe('2px')
  })

  it('shows no ring on a mouse click', async () => {
    await render(<Sample />)
    const button = document.querySelector<HTMLElement>('[data-testid=button]')
    if (button === null) throw new Error('no button')
    await userEvent.click(button)
    expect(document.activeElement).toBe(button)
    expect(getComputedStyle(button).outlineStyle).toBe('none')
  })
})
