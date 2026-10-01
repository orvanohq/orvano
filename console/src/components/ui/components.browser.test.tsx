import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { Toaster } from '@/components/ui/toast'
import { examples } from '@/dev/examples'
import { keyboardScripts, type Check } from './keyboard-scripts'

type Theme = 'dark' | 'light'
type Density = 'compact' | 'comfortable'

function setMode(theme: Theme, density: Density) {
  document.documentElement.dataset.theme = theme
  document.documentElement.dataset.density = density
  document.documentElement.style.colorScheme = theme
}

function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`Nothing matches ${what}`)
  return value
}

function holds(check: Check) {
  if ('focused' in check) {
    const element = must(document.querySelector(check.focused), check.focused)
    expect(document.activeElement, `${check.focused} has focus`).toBe(element)
  } else if ('attribute' in check) {
    const { selector, name, value } = check.attribute
    expect(
      must(document.querySelector(selector), selector).getAttribute(name),
      `${selector} ${name}`,
    ).toBe(value)
  } else if ('present' in check) {
    expect(document.querySelector(check.present), `${check.present} is present`).not.toBeNull()
  } else if ('absent' in check) {
    expect(document.querySelector(check.absent), `${check.absent} is absent`).toBeNull()
  } else if ('text' in check) {
    expect(
      must(document.querySelector(check.text.selector), check.text.selector).textContent,
    ).toContain(check.text.contains)
  } else {
    expect(
      must(document.querySelector<HTMLInputElement>(check.value.selector), check.value.selector)
        .value,
    ).toBe(check.value.is)
  }
}

/** Retries a check for a moment: overlays open and close over a few frames. */
async function eventually(check: Check) {
  await expect
    .poll(
      () => {
        try {
          holds(check)
          return true
        } catch (error) {
          return String(error)
        }
      },
      { timeout: 2000 },
    )
    .toBe(true)
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('every inventory component has an example and a script (AC-5)', () => {
  it('has a keyboard script for every example', () => {
    const covered = new Set(keyboardScripts.map((script) => script.example))
    expect(Object.keys(examples).filter((id) => !covered.has(id))).toEqual([])
  })
  it('has an example for every script', () => {
    expect(
      keyboardScripts.filter((script) => !(script.example in examples)).map((s) => s.example),
    ).toEqual([])
  })
})

/**
 * What keyboard focus draws on the focused element: the focus outline (spec 0005, Focus), or, for
 * a control inside an input group, the outline on the group's border. Empty when nothing shows.
 */
function focusRing(element: Element): string {
  const outline = (target: Element) => {
    const style = getComputedStyle(target)
    return style.outlineStyle === 'none' || parseFloat(style.outlineWidth) === 0
      ? ''
      : `${style.outlineStyle} ${style.outlineWidth}`
  }
  const group = element.closest('[data-slot=input-group]')
  return outline(element) || (group === null ? '' : outline(group))
}

describe('keyboard focus is always visible (spec 0005, Focus)', () => {
  it.each(Object.keys(examples))('%s', async (id) => {
    const screen = await render(<main>{must(examples[id], id).render()}</main>)
    const hidden: string[] = []
    const seen = new Set<Element>()
    for (let stop = 0; stop < 40; stop++) {
      await userEvent.tab()
      const active = document.activeElement
      if (active === null || active === document.body || seen.has(active)) break
      seen.add(active)
      if (focusRing(active) === '') hidden.push(active.outerHTML.slice(0, 160))
    }
    expect(hidden).toEqual([])
    await screen.unmount()
  })
})

describe.each<Theme>(['dark', 'light'])('keyboard scripts and axe in the %s theme', (theme) => {
  it.each(keyboardScripts.map((script) => [script.component, script] as const))(
    '%s',
    async (_name, script) => {
      setMode(theme, 'compact')
      const example = examples[script.example]
      const screen = await render(
        <main>
          {example.render()}
          <Toaster />
        </main>,
      )

      if (script.start !== undefined) {
        must(document.querySelector<HTMLElement>(script.start), script.start).focus()
      }
      for (const step of script.steps) {
        if (step.keys === '{Tab}') await userEvent.tab()
        else await userEvent.keyboard(step.keys)
        for (const check of step.then) await eventually(check)
      }

      // Overlays fade in over 150 ms; axe reads colors, so let them settle first.
      await new Promise((resolve) => setTimeout(resolve, 300))
      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
      expect(
        results.violations.map(
          (v) =>
            `${v.id}: ${v.nodes.map((n) => `${n.html.slice(0, 400)} ${JSON.stringify(n.any[0]?.data)}`).join(' | ')}`,
        ),
      ).toEqual([])
      await screen.unmount()
    },
  )
})
