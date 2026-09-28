import { describe, expect, it } from 'vitest'

import { meetsRole, orgNav, projectNav } from './nav.ts'

describe('meetsRole (AC-22)', () => {
  it('lets everyone through when there is no minimum', () => {
    expect(meetsRole('viewer', undefined)).toBe(true)
    expect(meetsRole(undefined, undefined)).toBe(true)
  })

  it('ranks viewer below developer below owner', () => {
    expect(meetsRole('viewer', 'developer')).toBe(false)
    expect(meetsRole('developer', 'developer')).toBe(true)
    expect(meetsRole('developer', 'owner')).toBe(false)
    expect(meetsRole('owner', 'owner')).toBe(true)
  })

  it('hides a gated entry until the role is known', () => {
    expect(meetsRole(undefined, 'owner')).toBe(false)
  })
})

describe('nav registry (AC-15)', () => {
  it('registers Projects for orgs and the project entries in order, with unique ids', () => {
    expect(orgNav.map((entry) => entry.label)).toEqual(['Projects'])
    // API keys and Platforms sit between Users and Settings (spec 0007, AC-11).
    expect(projectNav.map((entry) => entry.label)).toEqual([
      'Overview',
      'Users',
      'API keys',
      'Platforms',
      'Settings',
    ])
    const ids = [...orgNav, ...projectNav].map((entry) => entry.id)
    expect(new Set(ids).size).toBe(ids.length)
  })
})
