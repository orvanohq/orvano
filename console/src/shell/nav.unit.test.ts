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
    // Members sits between Projects and Settings, for every role (spec 0008, AC-15).
    expect(orgNav.map((entry) => entry.label)).toEqual(['Projects', 'Members', 'Settings'])
    expect(orgNav.find((entry) => entry.id === 'members')?.minRole).toBeUndefined()
    // API keys and Platforms sit between Users and Settings (spec 0007, AC-11); Email sits below
    // Users (spec 0009, Screens).
    expect(projectNav.map((entry) => entry.label)).toEqual([
      'Overview',
      'Users',
      'Email',
      'API keys',
      'Platforms',
      'Settings',
    ])
    const ids = [...orgNav, ...projectNav].map((entry) => entry.id)
    expect(new Set(ids).size).toBe(ids.length)
  })
})

describe('org Settings entry (spec 0007, AC-2)', () => {
  it('shows only to owners', () => {
    const settings = orgNav.find((entry) => entry.to === '/orgs/$orgId/settings')
    expect(settings?.minRole).toBe('owner')
    expect(meetsRole('developer', settings?.minRole)).toBe(false)
    expect(meetsRole('owner', settings?.minRole)).toBe(true)
  })
})
