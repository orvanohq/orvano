import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { PreferencesProvider, usePreferences } from '@/lib/preferences'

function Picker() {
  const { theme, density, setTheme, setDensity } = usePreferences()
  return (
    <>
      <output data-testid="theme">{theme}</output>
      <output data-testid="density">{density}</output>
      <button
        onClick={() => {
          setTheme('light')
        }}
      >
        Light
      </button>
      <button
        onClick={() => {
          setTheme('dark')
        }}
      >
        Dark
      </button>
      <button
        onClick={() => {
          setTheme('system')
        }}
      >
        System
      </button>
      <button
        onClick={() => {
          setDensity('comfortable')
        }}
      >
        Comfortable
      </button>
      <button
        onClick={() => {
          setDensity('compact')
        }}
      >
        Compact
      </button>
    </>
  )
}

async function renderPicker() {
  await render(
    <PreferencesProvider>
      <Picker />
    </PreferencesProvider>,
  )
}

async function press(name: string) {
  const button = [...document.querySelectorAll('button')].find((item) => item.textContent === name)
  if (button === undefined) throw new Error(`No button named ${name}`)
  await userEvent.click(button)
}

const shown = (testId: string) => document.querySelector(`[data-testid=${testId}]`)?.textContent
const html = document.documentElement

/** The operating system's color scheme, which a test can flip while the page is open. */
function fakeOperatingSystem(startsDark: boolean) {
  let dark = startsDark
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query) =>
      ({
        media: query,
        get matches() {
          return dark
        },
        addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
          listeners.add(listener)
        },
        removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
          listeners.delete(listener)
        },
      }) as unknown as MediaQueryList,
  )
  return {
    prefersDark: (next: boolean) => {
      dark = next
      for (const listener of listeners) listener({ matches: next } as MediaQueryListEvent)
    },
    listenerCount: () => listeners.size,
  }
}

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
  delete html.dataset.theme
  delete html.dataset.density
  html.style.colorScheme = ''
})

describe('theme (AC-2)', () => {
  it('is dark when nothing is stored', async () => {
    await renderPicker()
    expect(shown('theme')).toBe('dark')
  })

  it('reads the stored choice', async () => {
    window.localStorage.setItem('orvano.theme', 'light')
    await renderPicker()
    expect(shown('theme')).toBe('light')
  })

  it('falls back to dark for a stored value it does not know', async () => {
    window.localStorage.setItem('orvano.theme', 'purple')
    await renderPicker()
    expect(shown('theme')).toBe('dark')
  })

  it('applies a chosen theme to the page and remembers it', async () => {
    await renderPicker()
    await press('Light')
    expect(html.dataset.theme).toBe('light')
    expect(html.style.colorScheme).toBe('light')
    expect(window.localStorage.getItem('orvano.theme')).toBe('light')
    await press('Dark')
    expect(html.dataset.theme).toBe('dark')
  })

  it('resolves System through the operating system setting', async () => {
    fakeOperatingSystem(false)
    await renderPicker()
    await press('System')
    expect(html.dataset.theme).toBe('light')
    expect(window.localStorage.getItem('orvano.theme')).toBe('system')
  })

  it('follows the operating system live while the choice is System, without a reload', async () => {
    const system = fakeOperatingSystem(false)
    await renderPicker()
    await press('System')

    system.prefersDark(true)
    expect(html.dataset.theme).toBe('dark')
    system.prefersDark(false)
    expect(html.dataset.theme).toBe('light')
  })

  it('stops following the operating system once a fixed theme is chosen', async () => {
    const system = fakeOperatingSystem(true)
    await renderPicker()
    await press('System')
    await press('Dark')

    system.prefersDark(false)

    expect(html.dataset.theme).toBe('dark')
    expect(system.listenerCount()).toBe(0)
  })
})

describe('density (AC-3)', () => {
  it('is compact when nothing is stored', async () => {
    await renderPicker()
    expect(shown('density')).toBe('compact')
  })

  it('falls back to compact for a stored value it does not know', async () => {
    window.localStorage.setItem('orvano.density', 'huge')
    await renderPicker()
    expect(shown('density')).toBe('compact')
  })

  it('applies a chosen density to the page and remembers it', async () => {
    await renderPicker()
    await press('Comfortable')
    expect(html.dataset.density).toBe('comfortable')
    expect(window.localStorage.getItem('orvano.density')).toBe('comfortable')
    await press('Compact')
    expect(html.dataset.density).toBe('compact')
  })
})

describe('when browser storage is blocked', () => {
  it('starts from the defaults instead of throwing', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    await renderPicker()
    expect(shown('theme')).toBe('dark')
    expect(shown('density')).toBe('compact')
  })

  it('still applies a choice for this visit', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    await renderPicker()
    await press('Light')
    expect(html.dataset.theme).toBe('light')
    expect(shown('theme')).toBe('light')
  })
})
