import { readFileSync } from 'node:fs'
import { wcagContrast, parse } from 'culori'
import { describe, expect, it } from 'vitest'

import { contrastPairs } from './contrast-pairs.ts'

const css = readFileSync(new URL('./tokens.css', import.meta.url), 'utf8')

type Theme = 'dark' | 'light'

/** The custom properties a selector block declares. */
function declarations(selector: string): Map<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start === -1) throw new Error(`tokens.css has no ${selector} block`)
  const end = css.indexOf('\n}', start)
  const map = new Map<string, string>()
  for (const match of css.slice(start, end).matchAll(/--([\w-]+):\s*([^;]+);/g)) {
    map.set(match[1], match[2].trim())
  }
  return map
}

const dark = declarations(':root')
const light = declarations(":root[data-theme='light']")

/** A token's OKLCH value for a theme, following `var(--x)` aliases and the dark fallback. */
function resolve(theme: Theme, name: string): string {
  const raw = (theme === 'light' ? light.get(name) : undefined) ?? dark.get(name)
  if (raw === undefined) throw new Error(`no token --${name}`)
  const alias = /^var\(--([\w-]+)\)$/.exec(raw)
  return alias?.[1] === undefined ? raw : resolve(theme, alias[1])
}

describe.each<Theme>(['dark', 'light'])('contrast in the %s theme (AC-4)', (theme) => {
  it.each(contrastPairs)(
    '$foreground on $background meets $ratio:1',
    ({ foreground, background, ratio }) => {
      const fg = parse(resolve(theme, foreground))
      const bg = parse(resolve(theme, background))
      if (fg === undefined || bg === undefined) throw new Error('could not parse a token color')
      expect(wcagContrast(fg, bg)).toBeGreaterThanOrEqual(ratio)
    },
  )
})
