import { OrvanoError } from '@orvano/console-client'
import { describe, expect, it } from 'vitest'

import { describeError, isNotFound } from './errors.ts'

describe('isNotFound (AC-19)', () => {
  it('is true for a 404 that carries the code asked about', () => {
    const error = new OrvanoError(404, 'project_not_found', 'No such project.', null)
    expect(isNotFound(error, 'project_not_found')).toBe(true)
  })

  it('is true when any of several codes matches', () => {
    const error = new OrvanoError(404, 'not_found', 'No such org.', null)
    expect(isNotFound(error, 'project_not_found', 'not_found')).toBe(true)
  })

  it('is false for a 404 with a different code', () => {
    const error = new OrvanoError(404, 'route_missing', 'No such route.', null)
    expect(isNotFound(error, 'project_not_found', 'not_found')).toBe(false)
  })

  it('is false for the right code on a different status', () => {
    const error = new OrvanoError(403, 'project_not_found', 'Forbidden.', null)
    expect(isNotFound(error, 'project_not_found')).toBe(false)
  })

  it('is false for anything that is not an API error, even one shaped like it', () => {
    expect(isNotFound(new Error('project_not_found'), 'project_not_found')).toBe(false)
    expect(isNotFound({ status: 404, code: 'not_found' }, 'not_found')).toBe(false)
    expect(isNotFound(undefined, 'not_found')).toBe(false)
  })
})

describe('describeError (AC-21)', () => {
  it('gives an API error its message, code, and request ID', () => {
    const error = new OrvanoError(503, 'service_unavailable', 'The API is unavailable.', 'req-42')
    expect(describeError(error)).toEqual({
      message: 'The API is unavailable.',
      code: 'service_unavailable',
      requestId: 'req-42',
    })
  })

  it('keeps a missing request ID as null', () => {
    const error = new OrvanoError(500, 'internal', 'Something broke.', null)
    expect(describeError(error).requestId).toBeNull()
  })

  it('gives a network failure its message with no code or request ID', () => {
    expect(describeError(new TypeError('Failed to fetch'))).toEqual({
      message: 'Failed to fetch',
      code: null,
      requestId: null,
    })
  })

  it('falls back to a plain sentence for an error with no message', () => {
    expect(describeError(new Error('')).message).toBe('Something went wrong.')
  })

  it.each([
    ['a thrown string', 'boom'],
    ['undefined', undefined],
    ['a JSON body', { status: 500, body: '{"detail":"secret"}' }],
  ])('never surfaces %s, only the plain sentence', (_name, thrown) => {
    expect(describeError(thrown)).toEqual({
      message: 'Something went wrong.',
      code: null,
      requestId: null,
    })
  })
})
