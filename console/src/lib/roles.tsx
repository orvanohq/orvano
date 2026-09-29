import { useQuery } from '@tanstack/react-query'
import { useParams } from '@tanstack/react-router'
import type { ReactNode } from 'react'

import { orgQuery, projectQuery } from '@/lib/queries'
import { meetsRole } from '@/shell/nav'
import type { OrgRole } from '@orvano/console-client'

/**
 * Your role in the org in context: the org from the URL, or the project's org in project context.
 * Projects have no role of their own (spec 0003). Undefined until it loads.
 */
export function useOrgRole(): OrgRole | undefined {
  const { orgId, projectId } = useParams({ strict: false })
  const project = useQuery({ ...projectQuery(projectId ?? ''), enabled: projectId !== undefined })
  const resolvedOrgId = orgId ?? project.data?.orgId
  const org = useQuery({ ...orgQuery(resolvedOrgId ?? ''), enabled: resolvedOrgId !== undefined })
  return org.data?.role
}

/**
 * Each role's name and one line description, from the contract's `OrgRole` docs (spec 0008). Typed
 * as a `Record`, so a new role fails the build until it is described here.
 */
export const roleInfo: Record<OrgRole, { label: string; description: string }> = {
  owner: {
    label: 'Owner',
    description: 'Everything, including deleting projects, managing members, and the org itself.',
  },
  developer: {
    label: 'Developer',
    description:
      'Creates and edits projects, API keys, and platforms; deletes only the keys they created.',
  },
  viewer: { label: 'Viewer', description: 'Reads only.' },
}

/** Roles in the order the pickers list them, highest first. */
export const orgRoles: readonly OrgRole[] = ['owner', 'developer', 'viewer']

/** The reason text for a role rule, for example "Owners only" or "Developers and owners only". */
export function roleReason(minRole: OrgRole): string {
  return minRole === 'owner' ? 'Owners only' : 'Developers and owners only'
}

/**
 * Hides owner only areas (panels, sections) from people whose role is too low. Presentation only:
 * the API's 403 stays the real guard. Actions your role can't take but should see use
 * `Button`'s `disabledReason` instead (AC-22).
 */
export function RoleGate({
  minRole,
  role,
  children,
}: {
  minRole: OrgRole
  /** Overrides the role from context, for tests and the catalog. */
  role?: OrgRole | undefined
  children: ReactNode
}) {
  if (role !== undefined) return meetsRole(role, minRole) ? <>{children}</> : null
  return <ContextGate minRole={minRole}>{children}</ContextGate>
}

function ContextGate({ minRole, children }: { minRole: OrgRole; children: ReactNode }) {
  return meetsRole(useOrgRole(), minRole) ? <>{children}</> : null
}
