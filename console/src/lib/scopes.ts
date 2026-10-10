import type { ApiKeyScope } from '@orvano/console-client'

/** What one API key scope grants, as the Create key form shows it. */
export interface ScopeInfo {
  /** The resource label a scope grid row shows, for example "Users". */
  resource: string
  access: 'read' | 'write'
  /** The scope's `@doc` in `contract/platform/api-keys.tsp`, copied (spec 0007, data model). */
  description: string
}

/**
 * Every API key scope. Typed against the generated `ApiKeyScope`, so a scope the contract adds fails
 * the build until it has an entry here.
 */
export const scopes: Record<ApiKeyScope, ScopeInfo> = {
  'users.read': { resource: 'Users', access: 'read', description: "Read a project's users." },
  'users.write': {
    resource: 'Users',
    access: 'write',
    description: "Create, change, and delete a project's users.",
  },
  'tables.read': {
    resource: 'Tables',
    access: 'read',
    description: "Read a project's databases and tables, with their columns.",
  },
  'tables.write': {
    resource: 'Tables',
    access: 'write',
    description: "Create, change, and delete a project's databases, tables, and columns.",
  },
  'rows.read': {
    resource: 'Rows',
    access: 'read',
    description: 'Read, list, and count the rows of any table in the project.',
  },
  'rows.write': {
    resource: 'Rows',
    access: 'write',
    description: 'Create, change, and delete the rows of any table in the project.',
  },
}

/** One row of the scope grid: a resource with its read and write scopes, when it has them. */
export interface ScopeRow {
  resource: string
  read: ApiKeyScope | undefined
  write: ApiKeyScope | undefined
}

/** The scope grid's rows, one per resource, in first seen order. */
export function scopeRows(): ScopeRow[] {
  const rows = new Map<string, ScopeRow>()
  for (const [scope, info] of Object.entries(scopes) as [ApiKeyScope, ScopeInfo][]) {
    const row = rows.get(info.resource) ?? {
      resource: info.resource,
      read: undefined,
      write: undefined,
    }
    row[info.access] = scope
    rows.set(info.resource, row)
  }
  return [...rows.values()]
}
