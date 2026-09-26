import type { OrgStatus, ProjectStatus } from '@orvano/console-client'

type Tone = 'success' | 'warning' | 'danger' | 'neutral'

/** The word and dot color for an org or project status; every status shows its word (spec 0005). */
export function statusLabel(status: OrgStatus | ProjectStatus): { label: string; tone: Tone } {
  switch (status) {
    case 'active':
      return { label: 'Active', tone: 'success' }
    case 'provisioning':
      return { label: 'Setting up', tone: 'warning' }
    case 'failed':
      return { label: 'Failed', tone: 'danger' }
    case 'deleting':
      return { label: 'Deleting', tone: 'neutral' }
  }
}
