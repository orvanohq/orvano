import type {
  ApiKey,
  ApiKeyScope,
  ConsoleAccount,
  ConsoleSignupMode,
  Invitation,
  InvitationPreview,
  Member,
  Org,
  OrgRole,
  Platform,
  PlatformType,
  Project,
} from '@orvano/console-client'

/*
 * An in memory stand in for the console API, for browser tests that render real pages. Install it
 * with `installFakeApi()` before the app's modules load: the console client keeps the `fetch` it
 * finds when its module loads, so import the app dynamically afterwards (see `renderApp`). It
 * imports only types, so importing it never loads the client.
 */

const now = '2026-06-01T10:00:00.000Z'

/** One request the console sent, for asserting on bodies and counts. */
export interface SentRequest {
  method: string
  path: string
  project: string | null
  body: unknown
}

/** A one time answer that replaces the fake's own for the next matching request. */
interface Answer {
  method: string
  path: RegExp
  body: unknown
}

/** A one time problem that replaces the fake's own answer for the next matching request. */
interface Failure {
  method: string
  path: RegExp
  status: number
  code: string
  detail: string
}

export interface FakeApi {
  account: ConsoleAccount
  /** False makes `consoleAccount.get` answer 401, as for someone signed out (spec 0008, AC-21). */
  signedIn: boolean
  orgs: Org[]
  /** Members by org ID. */
  members: Record<string, Member[]>
  /** Invitations by org ID. */
  invitations: Record<string, Invitation[]>
  /** What `consoleInvitations.preview` answers for any token; null answers 404. */
  preview: InvitationPreview | null
  /** Who may create a console account, as the install settings say. */
  consoleSignup: ConsoleSignupMode
  projects: Project[]
  apiKeys: ApiKey[]
  platforms: Platform[]
  requests: SentRequest[]
  /** Whether the install still waits for its first admin, as `consoleInstall.getSetup` answers. */
  setupRequired: boolean
  /** Makes the next `method` request whose path matches answer with this problem, once. */
  failNext: (method: string, path: RegExp, status: number, code: string, detail: string) => void
  /** Makes the next `method` request whose path matches answer 200 with this JSON body, once. */
  answerNext: (method: string, path: RegExp, body: unknown) => void
}

export const accountId = 'user00000000000000001'

/** An org you belong to with `role`. */
export function makeOrg(overrides: Partial<Org> & { id: string; role?: OrgRole }): Org {
  return {
    name: 'Acme',
    status: 'active',
    role: 'owner',
    deletedAt: null,
    purgeAfter: null,
    createdAt: now,
    updatedAt: now,
    ...overrides,
  }
}

export function makeProject(overrides: Partial<Project> & { id: string; orgId: string }): Project {
  return {
    name: 'Scenarios',
    status: 'active',
    deletedAt: null,
    purgeAfter: null,
    purgeFailedAt: null,
    createdAt: now,
    updatedAt: now,
    ...overrides,
  }
}

export function makeKey(overrides: Partial<ApiKey> & { id: string }): ApiKey {
  return {
    name: 'Deploy',
    prefix: 'orv_abcdefgh',
    scopes: ['users.read'],
    expiresAt: null,
    lastUsedAt: null,
    createdByUserId: accountId,
    createdBy: { id: accountId, name: 'Ada', email: 'ada@example.com' },
    createdAt: now,
    ...overrides,
  }
}

/** A member of an org. */
export function makeMember(overrides: Partial<Member> & { userId: string }): Member {
  return {
    name: null,
    email: `${overrides.userId}@example.com`,
    status: 'active',
    role: 'developer',
    joinedAt: now,
    ...overrides,
  }
}

/** An invitation to an org. */
export function makeInvitation(overrides: Partial<Invitation> & { id: string }): Invitation {
  return {
    email: 'grace@example.com',
    role: 'developer',
    invitedBy: { id: accountId, name: 'Ada', email: 'ada@example.com' },
    status: 'pending',
    expiresAt: '2026-06-08T10:00:00.000Z',
    createdAt: now,
    ...overrides,
  }
}

/** The invite link the fake hands out; it opens nothing real. */
export const fakeInviteUrl = 'http://localhost/invite#fakeInviteTokenForTestsOnly00000000000000'

export function makePlatform(
  overrides: Partial<Platform> & { id: string; type?: PlatformType },
): Platform {
  return {
    type: 'web',
    name: 'Web app',
    identifier: 'app.example.com',
    createdAt: now,
    updatedAt: now,
    ...overrides,
  }
}

function problem(status: number, code: string, detail: string): Response {
  return Response.json(
    { type: `https://orvano.dev/errors/${code}`, title: detail, status, code, detail },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

const page = (items: readonly unknown[]) => Response.json({ items, nextCursor: null })

let counter = 0
const nextId = (prefix: string) => `${prefix}${String(++counter).padStart(20 - prefix.length, '0')}`

/** Replaces `fetch` with the fake and returns its state, which tests read and change freely. */
export function installFakeApi(): FakeApi {
  const failures: Failure[] = []
  const answers: Answer[] = []
  const api: FakeApi = {
    account: {
      id: accountId,
      email: 'ada@example.com',
      emailVerified: true,
      name: 'Ada',
      status: 'active',
      metadata: {},
      createdAt: now,
      lastSignInAt: now,
      isInstallAdmin: false,
    },
    signedIn: true,
    orgs: [],
    members: {},
    invitations: {},
    preview: null,
    consoleSignup: 'invite',
    projects: [],
    apiKeys: [],
    platforms: [],
    requests: [],
    setupRequired: false,
    failNext: (method, path, status, code, detail) => {
      failures.push({ method, path, status, code, detail })
    },
    answerNext: (method, path, body) => {
      answers.push({ method, path, body })
    },
  }

  const orgById = (id: string) => api.orgs.find((org) => org.id === id)
  const projectById = (id: string | null) => api.projects.find((project) => project.id === id)
  const replace = <T extends { id: string }>(list: T[], next: T) => {
    list.splice(
      list.findIndex((item) => item.id === next.id),
      1,
      next,
    )
    return next
  }

  async function handle(request: Request): Promise<Response> {
    const url = new URL(request.url)
    const path = url.pathname
    const method = request.method
    const project = request.headers.get('X-Orvano-Project')
    const text = method === 'GET' || method === 'DELETE' ? '' : await request.text()
    const body: unknown = text === '' ? undefined : JSON.parse(text)
    api.requests.push({ method, path, project, body })
    const input = (body ?? {}) as Record<string, unknown>

    const failure = failures.findIndex((f) => f.method === method && f.path.test(path))
    if (failure !== -1) {
      const [f] = failures.splice(failure, 1) as [Failure]
      return problem(f.status, f.code, f.detail)
    }
    const answer = answers.findIndex((a) => a.method === method && a.path.test(path))
    if (answer !== -1) {
      const [a] = answers.splice(answer, 1) as [Answer]
      return Response.json(a.body)
    }

    if (path === '/v1/console/account' && method === 'GET') {
      return api.signedIn
        ? Response.json(api.account)
        : problem(401, 'console_session_required', 'Sign in first.')
    }
    if (path === '/v1/console/install/setup' && method === 'GET') {
      return Response.json({
        setupRequired: api.setupRequired,
        signupOpen: api.consoleSignup === 'open',
      })
    }
    if (path === '/v1/console/install/settings') {
      if (!api.account.isInstallAdmin) {
        return problem(403, 'forbidden', 'Only install admins can do this.')
      }
      if (method === 'PATCH') api.consoleSignup = input.consoleSignup as ConsoleSignupMode
      return Response.json({ consoleSignup: api.consoleSignup, updatedAt: now })
    }
    if (path === '/v1/console/account/session' && method === 'POST') {
      api.signedIn = true
      api.account = { ...api.account, email: String(input.email) }
      return Response.json(api.account, { status: 201 })
    }
    if (path === '/v1/console/account/session' && method === 'DELETE') {
      api.signedIn = false
      return new Response(null, { status: 204 })
    }
    // A sign up: the first admin's, an open one, or an invited one (which joins the preview's org).
    // The real server checks the setup token and the invitation; refuse them with failNext.
    if (path === '/v1/console/account' && method === 'POST') {
      api.account = {
        ...api.account,
        email: String(input.email),
        name: typeof input.name === 'string' ? input.name : null,
      }
      api.signedIn = true
      api.setupRequired = false
      api.orgs.push(makeOrg({ id: nextId('org'), name: `${api.account.name ?? 'Your'}'s org` }))
      if (typeof input.inviteToken === 'string' && api.preview !== null) {
        api.orgs.push(
          makeOrg({ id: api.preview.orgId, name: api.preview.orgName, role: api.preview.role }),
        )
      }
      return Response.json(api.account, { status: 201 })
    }
    if (path === '/v1/console/invitations/preview' && method === 'POST') {
      return api.preview === null
        ? problem(404, 'invitation_not_found', 'This invite link isn’t valid anymore.')
        : Response.json(api.preview)
    }
    if (path === '/v1/console/invitations/accept' && method === 'POST') {
      const preview = api.preview
      if (preview === null) {
        return problem(404, 'invitation_not_found', 'This invite link isn’t valid anymore.')
      }
      const existing = orgById(preview.orgId)
      const org =
        existing ?? makeOrg({ id: preview.orgId, name: preview.orgName, role: preview.role })
      if (existing === undefined) api.orgs.push(org)
      api.preview = null
      return Response.json({ org, alreadyMember: existing !== undefined })
    }
    if (path === '/v1/console/orgs' && method === 'GET') return page(api.orgs)
    if (path === '/v1/console/orgs' && method === 'POST') {
      const org = makeOrg({ id: nextId('org'), name: String(input.name) })
      api.orgs.push(org)
      return Response.json(org, { status: 201 })
    }

    let match = /^\/v1\/console\/orgs\/([^/]+)\/(members|invitations)(?:\/([^/]+))?$/.exec(path)
    if (match !== null) {
      const [, id = '', kind] = match
      const itemId = match.at(3)
      if (orgById(id) === undefined) return problem(404, 'not_found', 'No such org.')
      if (kind === 'members') {
        const members = (api.members[id] ??= [])
        if (itemId === undefined) return page(members)
        const member = members.find((item) => item.userId === itemId)
        if (member === undefined) return problem(404, 'not_found', 'No such member.')
        if (method === 'DELETE') {
          members.splice(members.indexOf(member), 1)
          if (itemId === accountId) api.orgs = api.orgs.filter((org) => org.id !== id)
          return new Response(null, { status: 204 })
        }
        const updated = { ...member, role: input.role as OrgRole }
        members.splice(members.indexOf(member), 1, updated)
        if (itemId === accountId) {
          const org = orgById(id)
          if (org !== undefined) replace(api.orgs, { ...org, role: updated.role })
        }
        return Response.json(updated)
      }
      const invitations = (api.invitations[id] ??= [])
      if (itemId === undefined && method === 'GET') return page(invitations)
      if (itemId === undefined && method === 'POST') {
        const email = String(input.email)
        const kept = invitations.filter((item) => item.email.toLowerCase() !== email.toLowerCase())
        const invitation = makeInvitation({
          id: nextId('inv'),
          email,
          role: input.role as OrgRole,
        })
        api.invitations[id] = [...kept, invitation]
        return Response.json({ invitation, url: fakeInviteUrl }, { status: 201 })
      }
      const found = invitations.find((item) => item.id === itemId)
      if (found === undefined) return problem(404, 'not_found', 'No such invitation.')
      invitations.splice(invitations.indexOf(found), 1)
      return new Response(null, { status: 204 })
    }

    match = /^\/v1\/console\/orgs\/([^/]+)(\/restore|\/projects)?$/.exec(path)
    if (match !== null) {
      const [, id = '', rest] = match
      const org = orgById(id)
      if (org === undefined) return problem(404, 'not_found', 'No such org.')
      if (rest === '/projects' && method === 'GET') {
        return page(api.projects.filter((item) => item.orgId === id))
      }
      if (rest === '/projects' && method === 'POST') {
        const created = makeProject({
          id: nextId('proj'),
          orgId: id,
          name: String(input.name),
          status: 'provisioning',
        })
        api.projects.push(created)
        return Response.json(created, { status: 201 })
      }
      if (rest === '/restore') {
        return Response.json(
          replace(api.orgs, { ...org, status: 'active', deletedAt: null, purgeAfter: null }),
        )
      }
      if (method === 'GET') return Response.json(org)
      if (method === 'PATCH') return Response.json(replace(api.orgs, { ...org, ...input }))
      if (method === 'DELETE') {
        if (api.projects.some((item) => item.orgId === id && item.status !== 'deleting')) {
          return problem(409, 'org_not_empty', 'Delete the org’s projects first.')
        }
        return Response.json(
          replace(api.orgs, {
            ...org,
            status: 'deleting',
            deletedAt: now,
            purgeAfter: '2026-07-01T10:00:00.000Z',
          }),
        )
      }
    }

    if (path.startsWith('/v1/console/project')) {
      const current = projectById(project)
      if (current === undefined) return problem(404, 'project_not_found', 'No such project.')
      if (path === '/v1/console/project') {
        if (method === 'GET') return Response.json(current)
        if (method === 'PATCH')
          return Response.json(replace(api.projects, { ...current, ...input }))
        if (method === 'DELETE') {
          return Response.json(
            replace(api.projects, {
              ...current,
              status: 'deleting',
              deletedAt: now,
              purgeAfter: '2026-07-01T10:00:00.000Z',
            }),
          )
        }
      }
      if (path === '/v1/console/project/restore' || path.endsWith('/retry-provisioning')) {
        return Response.json(
          replace(api.projects, {
            ...current,
            status: 'provisioning',
            deletedAt: null,
            purgeAfter: null,
            purgeFailedAt: null,
          }),
        )
      }
      if (path.endsWith('/retry-purge')) {
        return Response.json(replace(api.projects, { ...current, purgeFailedAt: null }))
      }
      if (path === '/v1/console/project/auth/keys') return Response.json({ keys: [] })
      if (path === '/v1/console/project/keys' && method === 'GET') {
        return page(url.searchParams.get('limit') === '1' ? api.apiKeys.slice(0, 1) : api.apiKeys)
      }
      if (path === '/v1/console/project/keys' && method === 'POST') {
        const key = makeKey({
          id: nextId('key'),
          name: String(input.name),
          scopes: input.scopes as ApiKeyScope[],
          expiresAt: typeof input.expiresAt === 'string' ? input.expiresAt : null,
        })
        api.apiKeys.push(key)
        return Response.json(
          { apiKey: key, secret: 'orv_fake_secret_for_tests_only' },
          { status: 201 },
        )
      }
      match = /^\/v1\/console\/project\/keys\/([^/]+)$/.exec(path)
      if (match !== null && method === 'DELETE') {
        const index = api.apiKeys.findIndex((key) => key.id === match?.[1])
        if (index === -1) return problem(404, 'not_found', 'No such key.')
        api.apiKeys.splice(index, 1)
        return new Response(null, { status: 204 })
      }
      if (path === '/v1/console/project/platforms' && method === 'GET') {
        return page(
          url.searchParams.get('limit') === '1' ? api.platforms.slice(0, 1) : api.platforms,
        )
      }
      if (path === '/v1/console/project/platforms' && method === 'POST') {
        const platform = makePlatform({
          id: nextId('plat'),
          type: input.type as PlatformType,
          name: String(input.name),
          identifier: String(input.identifier),
        })
        api.platforms.push(platform)
        return Response.json(platform, { status: 201 })
      }
      match = /^\/v1\/console\/project\/platforms\/([^/]+)$/.exec(path)
      if (match !== null) {
        const found = api.platforms.find((item) => item.id === match?.[1])
        if (found === undefined) return problem(404, 'not_found', 'No such platform.')
        if (method === 'DELETE') {
          api.platforms.splice(api.platforms.indexOf(found), 1)
          return new Response(null, { status: 204 })
        }
        return Response.json(
          replace(api.platforms, { ...found, ...input, updatedAt: '2026-06-02T10:00:00.000Z' }),
        )
      }
    }
    return problem(404, 'not_found', `The fake API has no ${method} ${path}.`)
  }

  globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) =>
    handle(new Request(input, init))
  return api
}
