import { createHash } from 'node:crypto'
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import { readEmailPreviewHeaders } from '../../email-preview-headers.ts'

// Spec 0009, AC-31 and AC-32: the frame page's one inline script is the only script its policy
// allows, by hash. Change the script and this test names the hash to write into
// deploy/gateway/email-preview-headers.caddy.

const page = readFileSync(
  fileURLToPath(new URL('../../public/frames/email-preview.html', import.meta.url)),
  'utf8',
)
const headers = readEmailPreviewHeaders()
const policy = headers.get('Content-Security-Policy') ?? ''

describe('the email preview frame page', () => {
  it('has one inline script, and the policy allows exactly its hash', () => {
    const scripts = [...page.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)]
    expect(scripts.map((script) => script[0].slice(0, 8))).toEqual(['<script>'])
    const hash = createHash('sha256')
      .update(scripts[0]?.[1] ?? '', 'utf8')
      .digest('base64')
    expect(policy).toContain(`script-src 'sha256-${hash}';`)
    expect(policy.match(/'sha256-/g)).toHaveLength(1)
  })

  it('carries every AC-32 header', () => {
    const hash = /'sha256-([^']+)'/.exec(policy)?.[1] ?? ''
    expect(policy).toBe(
      "default-src 'none'; script-src 'sha256-" +
        hash +
        "'; style-src 'unsafe-inline'; img-src https: data:; base-uri 'none'; form-action 'none'; frame-ancestors 'self'; sandbox allow-scripts allow-popups allow-popups-to-escape-sandbox",
    )
    expect(
      Object.fromEntries([...headers].filter(([name]) => name !== 'Content-Security-Policy')),
    ).toEqual({
      'X-Frame-Options': 'SAMEORIGIN',
      'Referrer-Policy': 'no-referrer',
      'X-Content-Type-Options': 'nosniff',
      'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=(), usb=()',
      'Cross-Origin-Opener-Policy': 'same-origin',
      'Cache-Control': 'no-cache',
    })
  })

  it('is static: the frames it needs are in its markup, and it imports nothing', () => {
    expect(page).toMatch(/^<!doctype html>\n<html lang="en">/)
    expect(page).toContain(
      '<iframe title="Email content" sandbox="allow-popups allow-popups-to-escape-sandbox"></iframe>',
    )
    expect(page).not.toMatch(/<(?:link|script)\b[^>]*\b(?:src|href)=/)
    expect(page).not.toContain('\r')
  })
})

describe('reading the headers file', () => {
  const write = (text: string) => {
    const file = join(mkdtempSync(join(tmpdir(), 'orvano-headers-')), 'headers.caddy')
    writeFileSync(file, text)
    return file
  }

  it('skips comments and blank lines', () => {
    const file = write('# a comment\n\nContent-Security-Policy "default-src \'none\'"\n')
    expect([...readEmailPreviewHeaders(file)]).toEqual([
      ['Content-Security-Policy', "default-src 'none'"],
    ])
  })

  it('refuses a file with no policy, naming it', () => {
    const file = write('X-Frame-Options "DENY"\n')
    expect(() => readEmailPreviewHeaders(file)).toThrow(`${file}: no Content-Security-Policy`)
  })

  it('refuses a line that is not Name "value", naming the line', () => {
    const file = write('Content-Security-Policy "a"\nX-Frame-Options DENY\n')
    expect(() => readEmailPreviewHeaders(file)).toThrow(`${file}:2: expected Name "value"`)
  })
})
