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
  it('registers Projects for orgs and Overview, Users, and Settings for projects, with unique ids', () => {
    expect(orgNav.map((entry) => entry.label)).toEqual(['Projects'])
    expect(projectNav.map((entry) => entry.label)).toEqual(['Overview', 'Users', 'Settings'])
    const ids = [...orgNav, ...projectNav].map((entry) => entry.id)
    expect(new Set(ids).size).toBe(ids.length)
  })
})
