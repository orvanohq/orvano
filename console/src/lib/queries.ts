import { infiniteQueryOptions, queryOptions, type QueryClient } from '@tanstack/react-query'
import { consoleApi, projectClient } from '@/lib/console-client'
import { isSessionError } from '@/lib/session'

/** Page size for the switchers; `/orgs` and the org page use smaller pages. */
export const switcherPageSize = 100

/** Query keys, one place (spec 0005, Query setup). */
export const keys = {
  account: ['console', 'account'] as const,
  /**
   * The same account for public pages that work signed in or out (`/invite`, `/sign-up`): its query
   * is `sessionOptional`, so a 401 means "signed out" and never redirects (spec 0008, AC-21).
   */
  accountOptional: ['console', 'account', 'optional'] as const,
  setup: ['console', 'install', 'setup'] as const,
  installSettings: ['console', 'install', 'settings'] as const,
  installSmtp: ['console', 'install', 'smtp'] as const,
  installEmails: ['console', 'install', 'emails'] as const,
  /** Holds no token: the invite page's token never enters a query key or cache (AC-20). */
  invitationPreview: ['console', 'invitations', 'preview'] as const,
  orgs: ['console', 'orgs'] as const,
  org: (orgId: string) => ['console', 'orgs', orgId] as const,
  orgProjects: (orgId: string) => ['console', 'orgs', orgId, 'projects'] as const,
  members: (orgId: string) => ['console', 'orgs', orgId, 'members'] as const,
  invitations: (orgId: string) => ['console', 'orgs', orgId, 'invitations'] as const,
  project: (projectId: string) => ['console', 'projects', projectId] as const,
  users: (projectId: string) => ['console', 'projects', projectId, 'users'] as const,
  user: (projectId: string, userId: string) =>
    ['console', 'projects', projectId, 'users', userId] as const,
  userSessions: (projectId: string, userId: string) =>
    ['console', 'projects', projectId, 'users', userId, 'sessions'] as const,
  signingKeys: (projectId: string) => ['console', 'projects', projectId, 'signing-keys'] as const,
  apiKeys: (projectId: string) => ['console', 'projects', projectId, 'keys'] as const,
  platforms: (projectId: string) => ['console', 'projects', projectId, 'platforms'] as const,
  smtp: (projectId: string) => ['console', 'projects', projectId, 'email', 'smtp'] as const,
  emails: (projectId: string) => ['console', 'projects', projectId, 'email', 'log'] as const,
  emailTemplates: (projectId: string) =>
    ['console', 'projects', projectId, 'email', 'templates'] as const,
  emailTemplate: (projectId: string, kind: string) =>
    ['console', 'projects', projectId, 'email', 'templates', kind] as const,
  /** Whether the project has any key: the overview's first page of one (spec 0007, AC-21). */
  anyApiKey: (projectId: string) => ['console', 'projects', projectId, 'keys', 'any'] as const,
  /** Whether the project has any platform (spec 0007, AC-21). */
  anyPlatform: (projectId: string) =>
    ['console', 'projects', projectId, 'platforms', 'any'] as const,
}

/**
 * Marks every list of your orgs stale (the switcher's and the `/orgs` page's), but not each org or
 * its projects, after an org is created, renamed, deleted, or restored.
 */
export function invalidateOrgLists(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({
    queryKey: keys.orgs,
    predicate: (query) => query.queryKey.length === 2 || typeof query.queryKey[2] === 'object',
  })
}

/** The signed in console account; the session guard's probe (spec 0005, AC-20). */
export function accountQuery() {
  return queryOptions({
    queryKey: keys.account,
    queryFn: ({ signal }) => consoleApi().consoleAccount.get({ signal }),
  })
}

/**
 * The signed in console account, or null when signed out, for public pages (spec 0008, AC-21). The
 * global session handler skips it (`meta.sessionOptional`), so a 401 here never redirects.
 */
export function optionalAccountQuery() {
  return queryOptions({
    queryKey: keys.accountOptional,
    queryFn: async ({ signal }) => {
      try {
        return await consoleApi().consoleAccount.get({ signal })
      } catch (error) {
        if (isSessionError(error)) return null
        throw error
      }
    },
    meta: { sessionOptional: true },
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

/** The install settings; install admins only (spec 0008, AC-24). */
export function installSettingsQuery() {
  return queryOptions({
    queryKey: keys.installSettings,
    queryFn: ({ signal }) => consoleApi().consoleInstall.getSettings({ signal }),
  })
}

/** The install's own SMTP settings; install admins only (spec 0009, AC-7). Never a password. */
export function installSmtpQuery() {
  return queryOptions({
    queryKey: keys.installSmtp,
    queryFn: ({ signal }) => consoleApi().consoleInstall.getSmtp({ signal }),
  })
}

/** The console's own emails (invites), newest first, 25 per page; install admins only (spec 0009, AC-21). */
export function installEmailsQuery() {
  return infiniteQueryOptions({
    queryKey: keys.installEmails,
    queryFn: ({ pageParam, signal }) =>
      consoleApi().consoleInstall.listEmails({ cursor: pageParam, limit: 25 }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/**
 * An org's members, oldest first, 25 per page (spec 0008, AC-15). A page can hold fewer items than
 * the limit, even none, while it still has a next cursor (AC-8).
 */
export function membersQuery(orgId: string) {
  return infiniteQueryOptions({
    queryKey: keys.members(orgId),
    queryFn: ({ pageParam, signal }) =>
      consoleApi().consoleMembers.list(orgId, { cursor: pageParam, limit: 25 }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

/**
 * An org's invitations, oldest first, 25 per page; owners only (spec 0008, AC-18). Holds
 * `Invitation` only: an invite link is never written to any query (AC-17).
 */
export function invitationsQuery(orgId: string) {
  return infiniteQueryOptions({
    queryKey: keys.invitations(orgId),
    queryFn: ({ pageParam, signal }) =>
      consoleApi().consoleInvitations.list(orgId, { cursor: pageParam, limit: 25 }, { signal }),
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
    // Check again every 2 seconds while any listed project is being set up, as the project page does.
    refetchInterval: (query) =>
      query.state.data?.pages.some((page) => page.items.some((p) => p.status === 'provisioning'))
        ? 2000
        : false,
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
export function usersQuery(projectId: string, email: string, emailVerified?: boolean, limit = 25) {
  return infiniteQueryOptions({
    queryKey: [...keys.users(projectId), { email, emailVerified, limit }] as const,
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consoleUsers.list(
        { email: email === '' ? undefined : email, emailVerified, cursor: pageParam, limit },
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

/** Whether the project has at least one API key, for the overview (spec 0007, AC-21). */
export function anyApiKeyQuery(projectId: string) {
  return queryOptions({
    queryKey: keys.anyApiKey(projectId),
    queryFn: async ({ signal }) => {
      const page = await projectClient(projectId).consoleApiKeys.list({ limit: 1 }, { signal })
      return page.items.length > 0
    },
  })
}

/** Whether the project has at least one platform, for the overview (spec 0007, AC-21). */
export function anyPlatformQuery(projectId: string) {
  return queryOptions({
    queryKey: keys.anyPlatform(projectId),
    queryFn: async ({ signal }) => {
      const page = await projectClient(projectId).consolePlatforms.list({ limit: 1 }, { signal })
      return page.items.length > 0
    },
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

/** A project's SMTP settings and whose settings it sends through (spec 0009, AC-4). Never a password. */
export function smtpQuery(projectId: string) {
  return queryOptions({
    queryKey: keys.smtp(projectId),
    queryFn: ({ signal }) => projectClient(projectId).consoleSmtp.get({ signal }),
  })
}

/** A project's four auth email templates, each marked Default or Custom (spec 0009, AC-8). */
export function emailTemplatesQuery(projectId: string) {
  return queryOptions({
    // Shares its prefix with the single templates, so one invalidation refreshes the list and the editor.
    queryKey: [...keys.emailTemplates(projectId), 'catalog'] as const,
    queryFn: ({ signal }) => projectClient(projectId).consoleEmailTemplates.getCatalog({ signal }),
  })
}

/** One template with its variables: the project's own version, or the default (spec 0009, AC-9). */
export function emailTemplateQuery(projectId: string, kind: string) {
  return queryOptions({
    queryKey: keys.emailTemplate(projectId, kind),
    queryFn: ({ signal }) => projectClient(projectId).consoleEmailTemplates.get(kind, { signal }),
  })
}

/** A project's emails of the last 30 days, newest first, 25 per page (spec 0009, AC-20). */
export function emailsQuery(projectId: string) {
  return infiniteQueryOptions({
    queryKey: keys.emails(projectId),
    queryFn: ({ pageParam, signal }) =>
      projectClient(projectId).consoleEmails.list({ cursor: pageParam, limit: 25 }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}
