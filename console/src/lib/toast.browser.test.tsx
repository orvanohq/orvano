import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { Toaster } from '@/components/ui/toast'
import { notifyError, notifySuccess } from '@/lib/toast'
import { OrvanoError } from '@orvano/console-client'

const toastTexts = () =>
  [...document.querySelectorAll('[data-base-ui-toast-root], [role=dialog]')].map(
    (toast) => toast.textContent,
  )
const text = () => document.body.textContent

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('toasts (AC-21)', () => {
  it('shows a success toast with its title and description', async () => {
    await render(<Toaster />)
    notifySuccess('Saved', 'Your changes are live.')
    await expect.poll(text).toContain('Saved')
    expect(text()).toContain('Your changes are live.')
  })

  it('removes a success toast after 5 seconds, and not before', async () => {
    await render(<Toaster />)
    notifySuccess('Project renamed')
    await expect.poll(text).toContain('Project renamed')

    await vi.advanceTimersByTimeAsync(4_900)
    expect(text()).toContain('Project renamed')

    await vi.advanceTimersByTimeAsync(200)
    await expect.poll(text).not.toContain('Project renamed')
  })

  it('keeps a failed action on screen until it is dismissed', async () => {
    await render(<Toaster />)
    notifyError('Could not delete the key')
    await expect.poll(text).toContain('Could not delete the key')

    await vi.advanceTimersByTimeAsync(60_000)

    expect(text()).toContain('Could not delete the key')
  })

  it('shows the error message of a failed action', async () => {
    await render(<Toaster />)
    notifyError(
      'Could not save',
      new OrvanoError(409, 'conflict', 'Someone else changed it.', null),
    )
    await expect.poll(text).toContain('Someone else changed it.')
  })

  it('shows a plain sentence, not the raw error, for something that is not an API error', async () => {
    await render(<Toaster />)
    notifyError('Could not save', { status: 500, body: '{"detail":"secret"}' })
    await expect.poll(text).toContain('Something went wrong.')
    expect(text()).not.toContain('secret')
  })

  it('removes a failed action once the person dismisses it', async () => {
    await render(<Toaster />)
    notifyError('Could not delete the key')
    await expect.poll(text).toContain('Could not delete the key')

    const dismiss = document.querySelector<HTMLElement>('[aria-label=Dismiss]')
    await userEvent.click(dismiss ?? document.body)

    await expect.poll(() => toastTexts().join('')).not.toContain('Could not delete the key')
  })

  it('moves focus to the toast region with F6, then Escape dismisses a focused toast', async () => {
    await render(<Toaster />)
    notifyError('Could not delete the key')
    await expect.poll(text).toContain('Could not delete the key')

    await userEvent.keyboard('{F6}')
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Notifications')

    // The next Tab stop inside the region is the toast itself.
    await userEvent.tab()
    expect(document.activeElement?.getAttribute('role')).toBe('dialog')

    await userEvent.keyboard('{Escape}')
    await expect.poll(() => toastTexts().join('')).not.toContain('Could not delete the key')
  })

  it('dismisses only the focused toast when several are stacked', async () => {
    await render(<Toaster />)
    notifyError('First failure')
    notifyError('Second failure')
    const toastCount = () => document.querySelectorAll('[role=dialog]').length
    await expect.poll(toastCount).toBe(2)

    await userEvent.keyboard('{F6}')
    await userEvent.tab()
    await userEvent.keyboard('{Escape}')

    await expect.poll(toastCount).toBe(1)
  })

  it('dismisses a toast with Escape while its Dismiss button has focus', async () => {
    await render(<Toaster />)
    notifyError('Could not delete the key')
    await expect.poll(text).toContain('Could not delete the key')

    await userEvent.keyboard('{F6}')
    await userEvent.tab()
    await userEvent.tab()
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Dismiss')

    await userEvent.keyboard('{Escape}')
    await expect.poll(() => toastTexts().join('')).not.toContain('Could not delete the key')
  })
})
