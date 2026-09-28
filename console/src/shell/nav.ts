import type { LinkProps } from '@tanstack/react-router'
import {
  FolderKanban,
  KeyRound,
  LayoutDashboard,
  MonitorSmartphone,
  Settings,
  UsersRound,
  type LucideIcon,
} from 'lucide-react'

import type { OrgRole } from '@orvano/console-client'

/** One sidebar entry. */
export interface NavEntry {
  id: string
  label: string
  icon: LucideIcon
  /** The route the entry opens; `$orgId` and `$projectId` come from the current context. */
  to: LinkProps['to']
  /** Match only this exact path (an overview must not stay current on its children). */
  exact?: boolean
  /** Entries above your role are hidden (an owner only area is `owner`). */
  minRole?: OrgRole
}

/**
 * The only place sidebar entries are declared (spec 0005, Nav registry). Each product row adds its
 * line to `projectNav`; nothing else in the console builds its own navigation.
 */
export const orgNav: readonly NavEntry[] = [
  { id: 'projects', label: 'Projects', icon: FolderKanban, to: '/orgs/$orgId', exact: true },
  {
    id: 'org-settings',
    label: 'Settings',
    icon: Settings,
    to: '/orgs/$orgId/settings',
    minRole: 'owner',
  },
]

/** Overview first, then every product entry; products show only while the project is active. */
export const projectNav: readonly NavEntry[] = [
  {
    id: 'overview',
    label: 'Overview',
    icon: LayoutDashboard,
    to: '/projects/$projectId',
    exact: true,
  },
  { id: 'users', label: 'Users', icon: UsersRound, to: '/projects/$projectId/users' },
  { id: 'keys', label: 'API keys', icon: KeyRound, to: '/projects/$projectId/keys' },
  {
    id: 'platforms',
    label: 'Platforms',
    icon: MonitorSmartphone,
    to: '/projects/$projectId/platforms',
  },
  { id: 'settings', label: 'Settings', icon: Settings, to: '/projects/$projectId/settings' },
]

const roleRank: Record<OrgRole, number> = { viewer: 0, developer: 1, owner: 2 }

/** True when `role` is at least `minRole`; no `minRole` means everyone. */
export function meetsRole(role: OrgRole | undefined, minRole: OrgRole | undefined): boolean {
  if (minRole === undefined) return true
  if (role === undefined) return false
  return roleRank[role] >= roleRank[minRole]
}
