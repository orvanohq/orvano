import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query'
import { consoleApi, projectClient } from '@/lib/console-client'

/** Page size for the switchers; `/orgs` and the org page use smaller pages. */
export const switcherPageSize = 100

/** Query keys, one place (spec 0005, Query setup). */
export const keys = {
  orgs: ['console', 'orgs'] as const,
  org: (orgId: string) => ['console', 'orgs', orgId] as const,
  orgProjects: (orgId: string) => ['console', 'orgs', orgId, 'projects'] as const,
  project: (projectId: string) => ['console', 'projects', projectId] as const,
}

/** The caller's orgs, paged by cursor. */
export function orgsQuery(limit: number = switcherPageSize) {
  return infiniteQueryOptions({
    queryKey: limit === switcherPageSize ? keys.orgs : ([...keys.orgs, { limit }] as const),
    queryFn: ({ pageParam, signal }) =>
      consoleApi().consoleOrgs.list({ cursor: pageParam, limit }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/** One org. */
export function orgQuery(orgId: string) {
  return queryOptions({
    queryKey: keys.org(orgId),
    queryFn: ({ signal }) => consoleApi().consoleOrgs.get(orgId, { signal }),
  })
}

/** An org's projects, paged by cursor. */
export function orgProjectsQuery(orgId: string, limit: number = switcherPageSize) {
  return infiniteQueryOptions({
    queryKey:
      limit === switcherPageSize
        ? keys.orgProjects(orgId)
        : ([...keys.orgProjects(orgId), { limit }] as const),
    queryFn: ({ pageParam, signal }) =>
      consoleApi().consoleProjects.list(orgId, { cursor: pageParam, limit }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/** One project, in any state, through the client bound to it. */
export function projectQuery(projectId: string) {
  return queryOptions({
    queryKey: keys.project(projectId),
    queryFn: ({ signal }) => projectClient(projectId).consoleProjects.get({ signal }),
  })
}
