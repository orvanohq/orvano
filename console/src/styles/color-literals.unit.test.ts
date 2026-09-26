import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const srcDir = fileURLToPath(new URL('..', import.meta.url))
const literal =
  /#[0-9a-fA-F]{8}\b|#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3,4}\b|\b(?:rgba?|hsla?|oklch|oklab|lab|lch|hwb)\(/

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    return statSync(path).isDirectory() ? files(path) : [path]
  })
}

describe('tokens only (AC-1)', () => {
  it('has no color literal in console/src outside tokens.css', () => {
    const offenders = files(srcDir)
      .filter((path) => /\.(tsx?|css)$/.test(path))
      .filter((path) => !/\.test\.tsx?$/.test(path) && !path.endsWith('routeTree.gen.ts'))
      .filter((path) => !path.endsWith(join('styles', 'tokens.css')))
      .filter((path) => literal.test(readFileSync(path, 'utf8')))
      .map((path) => relative(srcDir, path))
    expect(offenders).toEqual([])
  })
})
