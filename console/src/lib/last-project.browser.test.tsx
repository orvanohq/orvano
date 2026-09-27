import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { clearLastProject, getLastProject, setLastProject } from '@/lib/last-project'

const key = 'orvano.lastProject'
const projectId = 'scenarios0000000000a'

beforeEach(() => {
  window.localStorage.clear()
})

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
})

describe('the last opened project (AC-12)', () => {
  it('has none until a project is opened', () => {
    expect(getLastProject()).toBeUndefined()
  })

  it('remembers the project a person opened', () => {
    setLastProject(projectId)
    expect(getLastProject()).toBe(projectId)
    expect(window.localStorage.getItem(key)).toBe(projectId)
  })

  it('replaces the remembered project with the newest one', () => {
    setLastProject(projectId)
    setLastProject('other0000000000000b')
    expect(getLastProject()).toBe('other0000000000000b')
  })

  it.each([
    ['a path', '../evil'],
    ['capital letters', 'ABC123'],
    ['a space', 'has space'],
    ['nothing', ''],
    ['more than 60 characters', 'a'.repeat(61)],
  ])('ignores a stored value that is %s, so `/` never redirects on it', (_name, stored) => {
    window.localStorage.setItem(key, stored)
    expect(getLastProject()).toBeUndefined()
  })

  it('does not store a value that is not a project ID', () => {
    setLastProject('../evil')
    expect(window.localStorage.getItem(key)).toBeNull()
  })
})

describe('forgetting a project that no longer exists (AC-19)', () => {
  it('forgets the project when it is the remembered one', () => {
    setLastProject(projectId)
    clearLastProject(projectId)
    expect(getLastProject()).toBeUndefined()
  })

  it('keeps the remembered project when a different one is forgotten', () => {
    setLastProject(projectId)
    clearLastProject('other0000000000000b')
    expect(getLastProject()).toBe(projectId)
  })
})

describe('when browser storage is blocked', () => {
  it('reads as no project instead of throwing', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    expect(getLastProject()).toBeUndefined()
  })

  it('opens a project without throwing', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    expect(() => {
      setLastProject(projectId)
    }).not.toThrow()
  })

  it('forgets a project without throwing', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    expect(() => {
      clearLastProject(projectId)
    }).not.toThrow()
  })
})
