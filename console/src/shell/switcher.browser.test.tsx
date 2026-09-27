import type { ComponentProps } from 'react'
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

const orgs: SwitcherItem[] = [
  { id: 'a', name: 'Acme Robotics', status: 'active' },
  { id: 'b', name: 'Beta Labs', status: 'active' },
  { id: 'c', name: 'Old Co', status: 'deleting' },
]

async function renderSwitcher(overrides: Partial<ComponentProps<typeof Switcher>> = {}) {
  const props: ComponentProps<typeof Switcher> = {
    label: 'Switch org',
    placeholder: 'Select org',
    current: undefined,
    items: orgs,
    hasNextPage: false,
    isFetchingNextPage: false,
    fetchNextPage: vi.fn(),
    onPick: vi.fn(),
    ...overrides,
  }
  await render(<Switcher {...props} />)
  return props
}

const search = () =>
  document.querySelector<HTMLInputElement>('input[aria-label="Search: Switch org"]')
const options = () => [...document.querySelectorAll('[role=option]')]
const optionNames = () => options().map((option) => option.textContent)
const popupText = () => document.querySelector('[role=listbox]')?.parentElement?.textContent ?? ''

async function openAndType(typed: string) {
  await userEvent.click(trigger())
  await expect.poll(search).not.toBeNull()
  await userEvent.type(search() ?? document.body, typed)
}

describe('Switcher filtering (AC-13)', () => {
  it('lists everything loaded when nothing is typed', async () => {
    await renderSwitcher()
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)
  })

  it.each([
    ['LAB', ['Beta Labs']],
    ['obot', ['Acme Robotics']],
    ['O', ['Acme Robotics', 'Old Co']],
  ])('filters by name, ignoring case, anywhere in the name: "%s"', async (typed, expected) => {
    await renderSwitcher()
    await openAndType(typed)
    await expect
      .poll(() => optionNames().map((name) => name.replace('Deleting', '')))
      .toEqual(expected)
  })

  it('offers to load more when nothing loaded matches and more pages exist', async () => {
    const props = await renderSwitcher({ hasNextPage: true })
    await openAndType('zzz')
    await expect.poll(popupText).toContain('No match.')
    await new Promise((resolve) => setTimeout(resolve, 300))
    const before = vi.mocked(props.fetchNextPage).mock.calls.length

    const loadMore = [...document.querySelectorAll('button')].find(
      (button) => button.textContent === 'Load more to search further',
    )
    expect(loadMore).toBeDefined()
    await userEvent.click(loadMore ?? document.body)

    await expect
      .poll(() => vi.mocked(props.fetchNextPage).mock.calls.length)
      .toBeGreaterThan(before)
  })

  it('does not offer to load more when there are no more pages', async () => {
    await renderSwitcher({ hasNextPage: false })
    await openAndType('zzz')
    await expect.poll(popupText).toContain('No match.')
    expect(popupText()).not.toContain('Load more to search further')
  })

  it('does not offer to load more while something loaded matches', async () => {
    await renderSwitcher({ hasNextPage: true })
    await openAndType('beta')
    await expect.poll(() => options().length).toBe(1)
    expect(popupText()).not.toContain('Load more to search further')
  })
})

describe('Switcher rows (AC-13, AC-14)', () => {
  it('shows a Deleting badge on an item being deleted, and none on active ones', async () => {
    await renderSwitcher()
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)
    expect(optionNames()).toEqual(['Acme Robotics', 'Beta Labs', 'Old CoDeleting'])
  })

  it('dims the name of an item being deleted', async () => {
    await renderSwitcher()
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)
    const opacity = (option: Element | undefined) =>
      getComputedStyle(option?.querySelector('span') ?? document.body).opacity
    expect(opacity(options()[0])).toBe('1')
    expect(opacity(options()[2])).toBe('0.6')
  })

  it('names the current item on the trigger and marks it selected in the list', async () => {
    await renderSwitcher({ current: orgs[1] })
    expect(trigger().getAttribute('aria-label')).toBe('Switch org: Beta Labs')
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)
    expect(options().map((option) => option.getAttribute('aria-selected'))).toEqual([
      'false',
      'true',
      'false',
    ])
  })

  it('shows the footer slot for later rows, and nothing when there is none', async () => {
    await renderSwitcher({ footer: <a href="#create">Create org</a> })
    await userEvent.click(trigger())
    await expect
      .poll(() => document.querySelector('[data-slot=switcher-footer]')?.textContent)
      .toBe('Create org')
  })

  it('has no footer slot when none is given', async () => {
    await renderSwitcher()
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)
    expect(document.querySelector('[data-slot=switcher-footer]')).toBeNull()
  })
})

describe('Switcher keyboard and picking (AC-13)', () => {
  it.each([
    ['Enter', '{Enter}'],
    ['Space', ' '],
  ])('opens with %s', async (_name, key) => {
    await renderSwitcher()
    ;(trigger() as HTMLElement).focus()
    await userEvent.keyboard(key)
    await expect.poll(() => options().length).toBe(3)
  })

  it('moves with the arrows and picks with Enter', async () => {
    const props = await renderSwitcher()
    ;(trigger() as HTMLElement).focus()
    await userEvent.keyboard('{Enter}')
    await expect.poll(() => options().length).toBe(3)

    await userEvent.keyboard('{ArrowDown}{ArrowDown}{Enter}')

    await expect.poll(() => vi.mocked(props.onPick).mock.calls.length).toBe(1)
    expect(props.onPick).toHaveBeenCalledWith(orgs[1])
  })

  it('picks an item with a click', async () => {
    const props = await renderSwitcher()
    await userEvent.click(trigger())
    await expect.poll(() => options().length).toBe(3)

    await userEvent.click(options()[0] ?? document.body)

    expect(props.onPick).toHaveBeenCalledWith(orgs[0])
  })

  it('closes on Escape and returns focus to the trigger', async () => {
    await renderSwitcher()
    ;(trigger() as HTMLElement).focus()
    await userEvent.keyboard('{Enter}')
    await expect.poll(() => options().length).toBe(3)

    await userEvent.keyboard('{Escape}')

    await expect.poll(() => options().length).toBe(0)
    expect(document.activeElement).toBe(trigger())
  })
})
