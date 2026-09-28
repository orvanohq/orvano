import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query'
import { consoleApi, projectClient } from '@/lib/console-client'

/** Page size for the switchers; `/orgs` and the org page use smaller pages. */
export const switcherPageSize = 100

/** Query keys, one place (spec 0005, Query setup). */
export const keys = {
  account: ['console', 'account'] as const,
  setup: ['console', 'install', 'setup'] as const,
  orgs: ['console', 'orgs'] as const,
  org: (orgId: string) => ['console', 'orgs', orgId] as const,
  orgProjects: (orgId: string) => ['console', 'orgs', orgId, 'projects'] as const,
  project: (projectId: string) => ['console', 'projects', projectId] as const,
  users: (projectId: string) => ['console', 'projects', projectId, 'users'] as const,
  user: (projectId: string, userId: string) =>
    ['console', 'projects', projectId, 'users', userId] as const,
  userSessions: (projectId: string, userId: string) =>
    ['console', 'projects', projectId, 'users', userId, 'sessions'] as const,
  signingKeys: (projectId: string) => ['console', 'projects', projectId, 'signing-keys'] as const,
  apiKeys: (projectId: string) => ['console', 'projects', projectId, 'keys'] as const,
  platforms: (projectId: string) => ['console', 'projects', projectId, 'platforms'] as const,
}

/** The signed in console account; the session guard's probe (spec 0005, AC-20). */
export function accountQuery() {
  return queryOptions({
    queryKey: keys.account,
    queryFn: ({ signal }) => consoleApi().consoleAccount.get({ signal }),
  })
}

/** Whether the install still waits for its first admin (spec 0006, AC-22); needs no session. */
export function setupQuery() {
  return queryOptions({
    queryKey: keys.setup,
    queryFn: ({ signal }) => consoleApi().consoleInstall.getSetup({ signal }),
  })
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

/** A project's users, newest first, paged by cursor; `email` narrows to a prefix (spec 0004, AC-29). */
export function usersQuery(projectId: string, email: string, limit = 25) {
  return infiniteQueryOptions({
    queryKey: [...keys.users(projectId), { email, limit }] as const,
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consoleUsers.list(
        { email: email === '' ? undefined : email, cursor: pageParam, limit },
        { signal },
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/** One user of a project. */
export function userQuery(projectId: string, userId: string) {
  return queryOptions({
    queryKey: keys.user(projectId, userId),
    queryFn: ({ signal }) => projectClient(projectId).consoleUsers.get(userId, { signal }),
  })
}

/** A user's active sessions, newest first, paged by cursor. */
export function userSessionsQuery(projectId: string, userId: string) {
  return infiniteQueryOptions({
    queryKey: keys.userSessions(projectId, userId),
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consoleUsers.listSessions(
        userId,
        { cursor: pageParam, limit: 25 },
        { signal },
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/** A project's token signing keys, the active one first (spec 0004, AC-22). */
export function signingKeysQuery(projectId: string) {
  return queryOptions({
    queryKey: keys.signingKeys(projectId),
    queryFn: ({ signal }) => projectClient(projectId).consoleAuthKeys.list({ signal }),
  })
}

/**
 * A project's API keys, oldest first, 25 per page (spec 0007, AC-12). Holds `ApiKey` only: a new key's
 * secret is never written to any query (AC-15).
 */
export function apiKeysQuery(projectId: string) {
  return infiniteQueryOptions({
    queryKey: keys.apiKeys(projectId),
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consoleApiKeys.list({ cursor: pageParam, limit: 25 }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/** A project's platforms, oldest first, 25 per page (spec 0007, AC-17). */
export function platformsQuery(projectId: string) {
  return infiniteQueryOptions({
    queryKey: keys.platforms(projectId),
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consolePlatforms.list({ cursor: pageParam, limit: 25 }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}
