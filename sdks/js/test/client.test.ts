import { describe, expect, it, vi } from 'vitest'

import { Client, MemorySessionStore, Orvano, OrvanoError, sdkVersion } from '../src/index.js'
import type { ClientConfig } from '../src/index.js'
import { Client as ServerClient, Orvano as ServerOrvano } from '../src/server.js'
import { fakeFetch, health, problem, status } from './fake-fetch.js'

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }

function app(fetch: typeof globalThis.fetch, config: Partial<ClientConfig> = {}): Orvano {
  return new Orvano(new Client({ endpoint, fetch, logger: quiet, ...config }))
}

describe('headers', () => {
  it('sends the SDK name and version on every request (AC-11)', async () => {
    const { fetch, sent } = fakeFetch(health())

    await app(fetch).health.get()

    expect(sent[0]?.headers.get('X-Orvano-SDK')).toBe(`@orvano/js/${sdkVersion}`)
  })

  it('sends the project and the session token from the session store', async () => {
    const { fetch, sent } = fakeFetch(health())

    await app(fetch, { project: 'p1', session: new MemorySessionStore('t') }).health.get()

    expect(sent[0]?.headers.get('X-Orvano-Project')).toBe('p1')
    expect(sent[0]?.headers.get('X-Orvano-Session')).toBe('t')
  })

  it('never sends an API key from the app entry (AC-4)', async () => {
    const { fetch, sent } = fakeFetch(health())

    await app(fetch, { headers: {} }).health.get()

    expect(sent[0]?.headers.has('X-Orvano-Key')).toBe(false)
    expect(sent[0]?.headers.has('X-Orvano-Session')).toBe(false)
  })

  it('sends the API key from the server entry (AC-4)', async () => {
    const { fetch, sent } = fakeFetch(health())

    await new ServerOrvano(
      new ServerClient({ endpoint, fetch, logger: quiet, apiKey: 'test-server-key' }),
    ).health.get()

    expect(sent[0]?.headers.get('X-Orvano-Key')).toBe('test-server-key')
  })

  it.each([endpoint, `${endpoint}/`])(
    'joins %s and the operation path with one slash',
    async (base) => {
      const { fetch, sent } = fakeFetch(health())

      await app(fetch, { endpoint: base }).health.get()

      expect(sent[0]?.url).toBe(`${endpoint}/v1/health`)
    },
  )

  it('refuses an endpoint that is not an absolute URL', () => {
    expect(() => new Client({ endpoint: '/orvano' })).toThrow(TypeError)
  })

  it('leaves undefined query values out of the URL', async () => {
    const { fetch, sent } = fakeFetch(health())

    await new Client({ endpoint, fetch, logger: quiet }).request({
      method: 'GET',
      path: '/v1/things',
      query: { limit: 2, cursor: undefined },
    })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/things?limit=2`)
  })
})

describe('version warning (AC-11)', () => {
  it('warns once per client when the server runs another minor', async () => {
    const { fetch } = fakeFetch(health('0.99.0'))
    const warn = vi.fn()
    const orvano = app(fetch, { logger: { warn } })

    await orvano.health.get()
    await orvano.health.get()

    expect(warn).toHaveBeenCalledTimes(1)
    expect(warn.mock.calls[0]?.[0]).toContain('runs 0.99.0')
    expect(warn.mock.calls[0]?.[0]).toContain('@orvano/js 0.99.x')
  })

  it('stays quiet when only the patch version differs', async () => {
    const [major, minor] = sdkVersion.split('.')
    const { fetch } = fakeFetch(health(`${major ?? '0'}.${minor ?? '0'}.99`))
    const warn = vi.fn()

    await app(fetch, { logger: { warn } }).health.get()

    expect(warn).not.toHaveBeenCalled()
  })

  it('stays quiet when the server sends no version', async () => {
    const { fetch } = fakeFetch(() => Response.json({ status: 'ok', version: 'x' }))
    const warn = vi.fn()

    await app(fetch, { logger: { warn } }).health.get()

    expect(warn).not.toHaveBeenCalled()
  })
})

describe('retries (AC-14)', () => {
  it.each([503, 429])('retries a GET after a %i, honoring Retry-After', async (code) => {
    const { fetch, sent } = fakeFetch(status(code, '0'), health())

    const result = await app(fetch).health.get()

    expect(result.status).toBe('ok')
    expect(sent).toHaveLength(2)
  })

  it('gives up after maxRetries with the last status', async () => {
    const { fetch, sent } = fakeFetch(status(503, '0'))

    await expect(app(fetch, { maxRetries: 2 }).health.get()).rejects.toMatchObject({ status: 503 })
    expect(sent).toHaveLength(3)
  })

  it('does not retry other failures', async () => {
    const { fetch, sent } = fakeFetch(status(500))

    await expect(app(fetch).health.get()).rejects.toBeInstanceOf(OrvanoError)
    expect(sent).toHaveLength(1)
  })

  it('never retries a POST that is not marked idempotent', async () => {
    const { fetch, sent } = fakeFetch(status(503, '0'))

    await expect(
      new Client({ endpoint, fetch, logger: quiet }).request({
        method: 'POST',
        path: '/v1/things',
      }),
    ).rejects.toMatchObject({ status: 503 })
    expect(sent).toHaveLength(1)
  })

  it('retries a POST marked idempotent', async () => {
    const { fetch, sent } = fakeFetch(status(503, '0'), status(204))

    await new Client({ endpoint, fetch, logger: quiet }).request({
      method: 'POST',
      path: '/v1/things',
      idempotent: true,
    })

    expect(sent).toHaveLength(2)
  })
})

describe('timeouts and cancellation (AC-14)', () => {
  it('times out a call that takes too long', async () => {
    const { fetch } = fakeFetch('hang')

    await expect(app(fetch).health.get({ timeoutMs: 50 })).rejects.toMatchObject({
      name: 'TimeoutError',
    })
  })

  it('stops at once when the caller cancels', async () => {
    const { fetch } = fakeFetch('hang')
    const cancel = new AbortController()
    cancel.abort()

    await expect(app(fetch).health.get({ signal: cancel.signal })).rejects.toMatchObject({
      name: 'AbortError',
    })
  })
})

describe('errors (AC-6)', () => {
  it('maps a problem body to one error with its code, detail, and request ID', async () => {
    const { fetch } = fakeFetch(
      problem(
        409,
        {
          title: 'Conflict',
          status: 409,
          detail: 'Already there.',
          code: 'user_already_exists',
          requestId: 'body-id',
        },
        'header-id',
      ),
    )

    await expect(app(fetch).health.get()).rejects.toMatchObject({
      name: 'OrvanoError',
      status: 409,
      code: 'user_already_exists',
      message: 'Already there.',
      requestId: 'body-id',
    })
  })

  it('falls back to the title when a problem has no detail', async () => {
    const { fetch } = fakeFetch(
      problem(404, { title: 'Not Found', status: 404, code: 'not_found', requestId: 'r' }),
    )

    await expect(app(fetch).health.get()).rejects.toMatchObject({
      code: 'not_found',
      message: 'Not Found',
    })
  })

  it('uses unknown and the request ID header when the body is not a problem', async () => {
    const { fetch } = fakeFetch(
      () =>
        new Response('<html>Bad gateway</html>', {
          status: 502,
          headers: { 'X-Request-Id': 'header-id' },
        }),
    )

    await expect(app(fetch).health.get()).rejects.toMatchObject({
      status: 502,
      code: 'unknown',
      requestId: 'header-id',
    })
  })
})
