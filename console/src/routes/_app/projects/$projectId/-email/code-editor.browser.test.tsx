import axe from 'axe-core'
import { describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { CodeEditor } from './code-editor'

// Spec 0009, AC-30: an editor whose text is longer than its box scrolls, and axe must still find
// keyboard focusable content inside the scrolling area (scrollable-region-focusable).
describe('CodeEditor', () => {
  it.each([false, true])(
    'has no axe violations while it scrolls (read only: %s)',
    async (readOnly) => {
      const long = Array.from({ length: 80 }, (_, line) => `<p>Line ${String(line + 1)}</p>`).join(
        '\n',
      )
      const screen = await render(
        <main>
          <CodeEditor
            label="HTML"
            value={long}
            onChange={() => undefined}
            language="html"
            readOnly={readOnly}
            className="h-96"
          />
        </main>,
      )
      const host = document.querySelector('[data-slot=code-editor]')
      const scroller = host?.shadowRoot?.querySelector('.cm-scroller')
      await expect
        .poll(() => (scroller?.scrollHeight ?? 0) > (scroller?.clientHeight ?? 0))
        .toBe(true)

      const results = await axe.run(document.body)
      expect(results.violations.map((violation) => violation.id)).toEqual([])
      await screen.unmount()
    },
  )

  // Spec 0009, AC-30 and spec 0005, Focus: Tab alone reaches the editor in one stop, its border shows
  // the focus outline, and Escape then Tab goes on to the next control (the tabindex adds no stop).
  it.each([false, true])(
    'takes one Tab stop with a visible outline (read only: %s)',
    async (readOnly) => {
      const screen = await render(
        <main>
          <button type="button">Before</button>
          <CodeEditor
            label="HTML"
            value="<p>Hello</p>"
            onChange={() => undefined}
            language="html"
            readOnly={readOnly}
            className="h-40"
          />
          <button type="button">After</button>
        </main>,
      )
      const host = document.querySelector('[data-slot=code-editor]')
      const content = host?.shadowRoot?.querySelector('.cm-content')

      await userEvent.tab()
      expect(document.activeElement?.textContent).toBe('Before')
      await userEvent.tab()
      expect(document.activeElement).toBe(host)
      expect(host?.shadowRoot?.activeElement).toBe(content)
      const style = getComputedStyle(host ?? document.body)
      expect(style.outlineStyle).not.toBe('none')
      expect(parseFloat(style.outlineWidth)).toBeGreaterThan(0)

      await userEvent.keyboard('{Escape}{Tab}')
      expect(document.activeElement?.textContent).toBe('After')
      await screen.unmount()
    },
  )
})
