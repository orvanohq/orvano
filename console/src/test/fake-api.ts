import type {
  ApiKey,
  ApiKeyScope,
  AuthEmailKind,
  ConsoleAccount,
  ConsoleSignupMode,
  EmailLogEntry,
  EmailTemplate,
  EmailTemplateInput,
  Invitation,
  InvitationPreview,
  Member,
  MfaFactor,
  MfaStatus,
  Org,
  Passkey,
  OrgRole,
  Platform,
  PlatformType,
  Project,
  SmtpSettings,
  SmtpSettingsInput,
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
  /** The install's SMTP settings (spec 0009); with them, a new invitation answers `emailed: true`. */
  installSmtp: SmtpSettings | null
  /** The project's own SMTP settings (spec 0009, AC-4); they win over the install's. */
  projectSmtp: SmtpSettings | null
  /** The console's own email log, as `consoleInstall.listEmails` answers. */
  installEmails: EmailLogEntry[]
  /** The project email log, as `consoleEmails.list` answers for any project. */
  emails: EmailLogEntry[]
  /** The email templates a project edited, by kind (spec 0009); the rest are the fake's defaults. */
  emailTemplates: Partial<Record<AuthEmailKind, EmailTemplateInput & { updatedAt: string }>>
  /** Whether any SMTP server is set up; without one a template test answers 409 (spec 0009, AC-12). */
  emailConfigured: boolean
  projects: Project[]
  apiKeys: ApiKey[]
  platforms: Platform[]
  requests: SentRequest[]
  /** Whether the install still waits for its first admin, as `consoleInstall.getSetup` answers. */
  setupRequired: boolean
  /**
   * The factors a password sign in is challenged with (spec 0013, AC-41); null signs in at once.
   * The second step accepts any well formed factor; refuse one with `failNext`.
   */
  consoleMfa: MfaFactor[] | null
  /** The signed in account's MFA state, as `consoleAccount.getMfa` answers (spec 0013, AC-42). */
  accountMfa: MfaStatus
  /** The signed in account's passkeys. */
  accountPasskeys: Passkey[]
  /** Makes the next `method` request whose path matches answer with this problem, once. */
  failNext: (method: string, path: RegExp, status: number, code: string, detail: string) => void
  /** Makes the next `method` request whose path matches answer 200 with this JSON body, once. */
  answerNext: (method: string, path: RegExp, body: unknown) => void
}

export const accountId = 'user00000000000000001'

/** What the fake answers for a new authenticator app secret and for new recovery codes. */
export const fakeTotpSecret = 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP'
export const fakeRecoveryCodes = [
  'AAAAA-22222',
  'BBBBB-33333',
  'CCCCC-44444',
  'DDDDD-55555',
  'EEEEE-66666',
  'FFFFF-77777',
  'GGGGG-22222',
  'HHHHH-33333',
  'IIIII-44444',
  'JJJJJ-55555',
]

/** The five templates as the catalog lists them (spec 0009, Template variables; spec 0013, AC-31). */
export const fakeTemplates: Record<AuthEmailKind, { name: string; description: string }> = {
  verification: {
    name: 'Email verification',
    description: 'Sent to confirm a user owns their email address.',
  },
  recovery: {
    name: 'Password reset',
    description: 'Sent when a user asks to reset their password.',
  },
  magic_link: {
    name: 'Magic link',
    description: 'Sent when a user signs in with a link instead of a password.',
  },
  email_code: {
    name: 'Email code',
    description: 'Sent when a user signs in with a one time code.',
  },
  security_alert: {
    name: 'Security alert',
    description:
      "Sent when a user's sign in security changes: MFA on or off, a passkey added or removed, recovery codes made or used.",
  },
}

const sampleUrl = 'https://example.com/auth/confirm?token=sample'

/** A short default template: enough Liquid for a preview to have something to fill in. */
function defaultTemplate(kind: AuthEmailKind): EmailTemplateInput {
  if (kind === 'security_alert') {
    return {
      subject: 'Security alert for {{ project.name }}',
      html: '<h1>{{ alert }}</h1>\n<p>When: {{ occurred_at }}</p>',
      text: '{{ alert }} at {{ occurred_at }}',
    }
  }
  const action = kind === 'email_code' ? '{{ code }}' : '<a href="{{ action_url }}">Open</a>'
  return {
    subject: `${fakeTemplates[kind].name} for {{ project.name }}`,
    html: `<h1>${fakeTemplates[kind].name}</h1>\n<p>Hi {{ user.name }}, ${action}</p>`,
    text: `${fakeTemplates[kind].name}: ${kind === 'email_code' ? '{{ code }}' : '{{ action_url }}'}`,
  }
}

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
      emailVerifiedAt: now,
      name: 'Ada',
      status: 'active',
      metadata: {},
      createdAt: now,
      lastSignInAt: now,
      providers: [],
      hasPassword: true,
      mfaEnabled: false,
      isInstallAdmin: false,
    },
    signedIn: true,
    orgs: [],
    members: {},
    invitations: {},
    preview: null,
    consoleSignup: 'invite',
    installSmtp: null,
    projectSmtp: null,
    installEmails: [],
    emails: [],
    emailTemplates: {},
    emailConfigured: true,
    projects: [],
    apiKeys: [],
    platforms: [],
    requests: [],
    setupRequired: false,
    consoleMfa: null,
    accountMfa: {
      mfaEnabled: false,
      totpConfirmed: false,
      totpConfirmedAt: null,
      recoveryCodesRemaining: 0,
      passkeyCount: 0,
      factorsAvailable: ['totp', 'passkey'],
    },
    accountPasskeys: [],
    failNext: (method, path, status, code, detail) => {
      failures.push({ method, path, status, code, detail })
    },
    answerNext: (method, path, body) => {
      answers.push({ method, path, body })
    },
  }

  const templateKinds: readonly AuthEmailKind[] = [
    'verification',
    'recovery',
    'magic_link',
    'email_code',
    'security_alert',
  ]
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
    // The account's MFA and passkeys (spec 0013, AC-42). Step up refusals come from failNext.
    if (path === '/v1/console/account/mfa' && method === 'GET') return Response.json(api.accountMfa)
    if (path === '/v1/console/account/mfa/totp' && method === 'POST') {
      return Response.json(
        {
          secret: fakeTotpSecret,
          uri: `otpauth://totp/Orvano:ada%40example.com?secret=${fakeTotpSecret}&issuer=Orvano&algorithm=SHA1&digits=6&period=30`,
          expiresAt: now,
        },
        { status: 201 },
      )
    }
    if (path === '/v1/console/account/mfa/totp/confirm' && method === 'POST') {
      api.accountMfa = {
        ...api.accountMfa,
        mfaEnabled: true,
        totpConfirmed: true,
        totpConfirmedAt: now,
        recoveryCodesRemaining: 10,
      }
      return Response.json({ recoveryCodes: fakeRecoveryCodes })
    }
    if (path === '/v1/console/account/mfa/totp' && method === 'DELETE') {
      api.accountMfa = {
        ...api.accountMfa,
        mfaEnabled: false,
        totpConfirmed: false,
        totpConfirmedAt: null,
        recoveryCodesRemaining: 0,
      }
      return new Response(null, { status: 204 })
    }
    if (path === '/v1/console/account/mfa/recovery-codes' && method === 'POST') {
      api.accountMfa = { ...api.accountMfa, recoveryCodesRemaining: 10 }
      return Response.json({ codes: fakeRecoveryCodes }, { status: 201 })
    }
    if (path === '/v1/console/account/mfa/verify' && method === 'POST') {
      return new Response(null, { status: 204 })
    }
    if (path === '/v1/console/account/passkeys' && method === 'GET') {
      return Response.json({ items: api.accountPasskeys })
    }
    const accountPasskey = /^\/v1\/console\/account\/passkeys\/([^/]+)$/.exec(path)?.[1]
    if (accountPasskey !== undefined) {
      const found = api.accountPasskeys.find((passkey) => passkey.id === accountPasskey)
      if (found === undefined) return problem(404, 'passkey_not_found', 'No such passkey.')
      if (method === 'DELETE') {
        api.accountPasskeys = api.accountPasskeys.filter((passkey) => passkey !== found)
        api.accountMfa = { ...api.accountMfa, passkeyCount: api.accountPasskeys.length }
        return new Response(null, { status: 204 })
      }
      return Response.json(replace(api.accountPasskeys, { ...found, name: String(input.name) }))
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
    if (path === '/v1/console/install/smtp' || path === '/v1/console/install/emails') {
      if (!api.account.isInstallAdmin) {
        return problem(403, 'forbidden', 'Only install admins can do this.')
      }
      if (path === '/v1/console/install/emails') return page(api.installEmails)
      if (method === 'DELETE') {
        api.installSmtp = null
        return new Response(null, { status: 204 })
      }
      if (method === 'PUT') {
        const { password, ...rest } = input as unknown as SmtpSettingsInput
        api.installSmtp = { ...rest, hasPassword: password !== null, updatedAt: now }
        return Response.json(api.installSmtp)
      }
      return Response.json({ settings: api.installSmtp })
    }
    if (path === '/v1/console/account/session' && method === 'POST') {
      api.account = { ...api.account, email: String(input.email) }
      if (api.consoleMfa !== null) {
        const mfa = { ticket: '', factors: api.consoleMfa, expiresAt: now }
        return Response.json({ account: null, mfa }, { status: 201 })
      }
      api.signedIn = true
      return Response.json({ account: api.account, mfa: null }, { status: 201 })
    }
    if (path === '/v1/console/account/session/mfa' && method === 'POST') {
      api.signedIn = true
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
        return Response.json(
          { invitation, url: fakeInviteUrl, emailed: api.installSmtp !== null },
          { status: 201 },
        )
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
      if (path === '/v1/console/project/emails') return page(api.emails)
      if (path === '/v1/console/project/email/smtp/test' && method === 'POST') {
        return Response.json({ sentTo: api.account.email ?? '' })
      }
      if (path === '/v1/console/project/email/smtp') {
        if (method === 'DELETE') {
          api.projectSmtp = null
          return new Response(null, { status: 204 })
        }
        if (method === 'PUT') {
          const { password, ...rest } = input as unknown as SmtpSettingsInput
          // As the server does: a null password keeps the stored one.
          const hasPassword = password !== null || api.projectSmtp?.hasPassword === true
          api.projectSmtp = { ...rest, hasPassword, updatedAt: now }
          return Response.json(api.projectSmtp)
        }
        const install = api.installSmtp
        return Response.json({
          source: api.projectSmtp !== null ? 'project' : install !== null ? 'install' : 'none',
          settings: api.projectSmtp,
          installSender:
            install === null ? null : { email: install.fromEmail, name: install.fromName },
        })
      }
      if (path === '/v1/console/project/email/templates') {
        return Response.json({
          templates: templateKinds.map((kind) => ({
            kind,
            ...fakeTemplates[kind],
            isCustom: api.emailTemplates[kind] !== undefined,
            updatedAt: api.emailTemplates[kind]?.updatedAt ?? null,
          })),
        })
      }
      match = /^\/v1\/console\/project\/email\/templates\/([^/]+)(\/preview|\/test)?$/.exec(path)
      if (match !== null) {
        const kind = templateKinds.find((item) => item === match?.[1])
        if (kind === undefined) return problem(404, 'not_found', 'No such email template.')
        const values: Record<string, string> = {
          'project.name': current.name,
          'user.email': api.account.email ?? '',
          'user.name': api.account.name ?? '',
          ...(kind === 'email_code' ? { code: '428613' } : { action_url: sampleUrl }),
          expires_in_minutes: kind === 'email_code' ? '10' : '60',
        }
        const template = (): EmailTemplate => {
          const custom = api.emailTemplates[kind]
          const source = custom ?? defaultTemplate(kind)
          return {
            kind,
            locale: 'en',
            subject: source.subject,
            html: source.html,
            text: source.text,
            isCustom: custom !== undefined,
            updatedAt: custom?.updatedAt ?? null,
            variables: Object.entries(values).map(([name, sample]) => ({
              name,
              description: `What ${name} holds.`,
              sample,
            })),
          }
        }
        const action = match.at(2)
        if (action === undefined) {
          if (method === 'GET') return Response.json(template())
          if (method === 'DELETE') {
            api.emailTemplates = { ...api.emailTemplates, [kind]: undefined }
            return new Response(null, { status: 204 })
          }
        }
        // The real server's rule, in small: a name outside the template's variables is refused.
        const given = input as unknown as EmailTemplateInput
        const fill = (part: string, source: string): string | Response => {
          const unknown = [...source.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)].find(
            (found) => !Object.hasOwn(values, found[1]),
          )
          if (unknown !== undefined) {
            const line = source.slice(0, unknown.index).split('\n').length
            const detail = `${part}: line ${String(line)}: unknown variable ${unknown[1]}`
            return problem(422, 'template_invalid', detail)
          }
          return source.replace(/\{\{\s*([\w.]+)\s*\}\}/g, (_, name: string) => values[name])
        }
        if (given.subject.trim() === '') {
          return problem(400, 'invalid_request', 'subject: Enter a subject.')
        }
        const subject = fill('subject', given.subject.trim())
        if (subject instanceof Response) return subject
        const html = fill('html', given.html)
        if (html instanceof Response) return html
        const text = fill('text', given.text ?? given.html.replace(/<[^>]+>/g, ''))
        if (text instanceof Response) return text
        if (action === '/preview') return Response.json({ subject, html, text })
        if (action === '/test') {
          return api.emailConfigured
            ? Response.json({ sentTo: api.account.email ?? '' })
            : problem(409, 'email_not_configured', 'No email server is set up.')
        }
        api.emailTemplates = {
          ...api.emailTemplates,
          [kind]: {
            subject: given.subject.trim(),
            html: given.html,
            text: given.text,
            updatedAt: '2026-06-02T10:00:00.000Z',
          },
        }
        return Response.json(template())
      }
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
