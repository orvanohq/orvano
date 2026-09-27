import { OrvanoError } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { ErrorPanel } from '@/shell/error-panel'

const apiError = new OrvanoError(503, 'service_unavailable', 'The API is unavailable.', 'req-42')
const text = () => document.querySelector('[role=alert]')?.textContent ?? ''

describe('ErrorPanel (AC-21)', () => {
  it('announces itself as an alert with the message', async () => {
    await render(<ErrorPanel error={apiError} onRetry={() => undefined} />)
    expect(document.querySelectorAll('[role=alert]')).toHaveLength(1)
    expect(text()).toContain("This page didn't load")
    expect(text()).toContain('The API is unavailable.')
  })

  it('shows the code and the request ID of an API error', async () => {
    await render(<ErrorPanel error={apiError} onRetry={() => undefined} />)
    const rows = [...document.querySelectorAll('dt')].map((term) => [
      term.textContent,
      term.nextElementSibling?.textContent,
    ])
    expect(rows).toEqual([
      ['Code', 'service_unavailable'],
      ['Request ID', 'req-42'],
    ])
  })

  it('leaves out the request ID row when the server sent none', async () => {
    const error = new OrvanoError(500, 'internal', 'Something broke.', null)
    await render(<ErrorPanel error={error} onRetry={() => undefined} />)
    expect(text()).toContain('Code')
    expect(text()).not.toContain('Request ID')
  })

  it('shows only the message for a failure that is not an API error', async () => {
    await render(<ErrorPanel error={new TypeError('Failed to fetch')} onRetry={() => undefined} />)
    expect(text()).toContain('Failed to fetch')
    expect(document.querySelector('dl')).toBeNull()
  })

  it('never shows a stack trace', async () => {
    const error = new Error('boom')
    error.stack = 'Error: boom\n    at secretFunction (internal.ts:1:1)'
    await render(<ErrorPanel error={error} onRetry={() => undefined} />)
    expect(text()).not.toContain('secretFunction')
  })

  it('loads the page again when Retry is pressed', async () => {
    const onRetry = vi.fn()
    await render(<ErrorPanel error={apiError} onRetry={onRetry} />)

    await userEvent.click(document.querySelector('button') ?? document.body)

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('lets a keyboard user reach and press Retry', async () => {
    const onRetry = vi.fn()
    await render(<ErrorPanel error={apiError} onRetry={onRetry} />)

    await userEvent.tab()
    expect(document.activeElement?.textContent).toBe('Retry')
    await userEvent.keyboard('{Enter}')

    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('has no axe violations', async () => {
    await render(
      <main>
        <ErrorPanel error={apiError} onRetry={() => undefined} />
      </main>,
    )
    const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
