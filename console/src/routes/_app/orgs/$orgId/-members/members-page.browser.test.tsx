import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import {
  accountId,
  fakeInviteUrl,
  installFakeApi,
  makeInvitation,
  makeMember,
  makeOrg,
  type FakeApi,
} from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

// Spec 0008, AC-15 to AC-19 and AC-26: the org Members page, the Invite dialog and its link step,
// Pending invitations, and the row menu's change role, remove, and leave.

const api: FakeApi = installFakeApi()
const orgId = 'orgm0000000000000001'
const otherOrgId = 'orgm0000000000000002'
const grace = 'userm000000000000002'
const linus = 'userm000000000000003'

function seed(role: OrgRole, status: 'active' | 'deleting' = 'active') {
  api.orgs = [
    makeOrg({ id: orgId, name: 'Acme', role, status }),
    makeOrg({ id: otherOrgId, name: 'Beta' }),
  ]
  api.members = {
    [orgId]: [
      makeMember({ userId: accountId, name: 'Ada', email: 'ada@example.com', role }),
      makeMember({ userId: grace, name: 'Grace Hopper', email: 'grace@example.com' }),
      makeMember({ userId: linus, email: 'linus@example.com', role: 'viewer', status: 'blocked' }),
    ],
  }
  api.invitations = {
    [orgId]: [
      makeInvitation({ id: 'invm0000000000000001', email: 'new@example.com' }),
      makeInvitation({
        id: 'invm0000000000000002',
        email: 'old@example.com',
        role: 'viewer',
        status: 'expired',
        invitedBy: null,
      }),
    ],
  }
  api.requests = []
}

const text = () => document.body.textContent
const dialog = () => document.querySelector('[data-slot=dialog-content]')
const alertDialog = () => document.querySelector('[role=alertdialog]')
const rowOf = (name: string) =>
  [...document.querySelectorAll('tbody tr')].find((row) => row.textContent.includes(name))
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
/** A button inside the open dialog, since the page's own Invite shares its name. */
const dialogButton = (name: string) =>
  [...(dialog()?.querySelectorAll<HTMLButtonElement>('button') ?? [])].find(
    (element) => element.textContent === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const sent = (method: string, path: RegExp) =>
  api.requests.filter((request) => request.method === method && path.test(request.path))

async function openMembers(role: OrgRole, status: 'active' | 'deleting' = 'active') {
  seed(role, status)
  const app = await renderApp(`/orgs/${orgId}/members`)
  await expect.poll(text).toContain('Grace Hopper')
  return app
}

async function openRowMenu(name: string) {
  await userEvent.click(button(`Actions for ${name}`) ?? document.body)
  await expect.poll(() => document.querySelector('[role=menu]')).not.toBeNull()
}

async function chooseMenuItem(label: string) {
  const item = [...document.querySelectorAll('[role=menuitem]')].find((element) =>
    element.textContent.startsWith(label),
  )
  await userEvent.click(item ?? document.body)
}

beforeEach(() => {
  setMode('dark', 'compact')
})

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the members table (AC-15)', () => {
  it('lists every member with a You badge, the email part when there is no name, and Blocked', async () => {
    await openMembers('viewer')
    expect(rowOf('Ada')?.textContent).toContain('You')
    expect(rowOf('Grace Hopper')?.textContent).toContain('Developer')
    expect(rowOf('linus@example.com')?.textContent).toContain('linus')
    expect(rowOf('linus@example.com')?.textContent).toContain('Blocked')
    expect(rowOf('Grace Hopper')?.textContent).not.toContain('Blocked')
  })

  it('adds Members to the org sidebar for every role, and titles the page', async () => {
    await openMembers('viewer')
    const link = document.querySelector<HTMLAnchorElement>(`a[href="/orgs/${orgId}/members"]`)
    expect(link?.textContent).toContain('Members')
    await expect.poll(() => document.title).toBe('Members · Acme · Orvano')
    expect(document.querySelector('h1#page-title')?.textContent).toBe('Members')
  })

  it('offers Load more instead of the empty state when a page comes back empty but more remain', async () => {
    seed('owner')
    // The server drops members whose account is gone after paging, so a page can be empty (AC-8).
    api.answerNext('GET', new RegExp(`/orgs/${orgId}/members$`), {
      items: [],
      nextCursor: 'more',
    })
    await renderApp(`/orgs/${orgId}/members`)
    await expect.poll(() => button('Load more')).toBeDefined()
    expect(text()).not.toContain('No members to show')

    await userEvent.click(button('Load more') ?? document.body)
    await expect.poll(text).toContain('Grace Hopper')
    expect(button('Load more')).toBeUndefined()
  })

  it('shows the empty state once no member is left to show and no page remains', async () => {
    seed('owner')
    api.answerNext('GET', new RegExp(`/orgs/${orgId}/members$`), { items: [], nextCursor: null })
    await renderApp(`/orgs/${orgId}/members`)
    await expect.poll(text).toContain('No members to show')
    expect(button('Load more')).toBeUndefined()
  })
})

describe('invite (AC-16, AC-17)', () => {
  it('is off for developers and viewers, and they never fetch invitations (AC-18)', async () => {
    await openMembers('developer')
    expect(button('Invite')?.getAttribute('aria-disabled')).toBe('true')
    expect(reason(button('Invite'))).toBe('Owners only')
    expect(text()).not.toContain('Pending invitations')
    expect(sent('GET', /\/invitations$/)).toHaveLength(0)
  })

  it('is off while the org is being deleted', async () => {
    await openMembers('owner', 'deleting')
    expect(reason(button('Invite'))).toBe('Restore the org first')
  })

  it('shows the link once, focused on its copy button, and keeps it nowhere after it closes', async () => {
    const { screen, queryClient } = await openMembers('owner')
    await userEvent.click(button('Invite') ?? document.body)
    await screen.getByLabelText('Email').fill('new@example.com')
    await screen.getByRole('radio', { name: 'Viewer' }).click()
    await userEvent.click(dialogButton('Invite') ?? document.body)

    await expect.poll(() => dialog()?.textContent).toContain('Share this invite link')
    expect(dialog()?.textContent).toContain(fakeInviteUrl)
    expect(dialog()?.textContent).toContain('new@example.com')
    expect(dialog()?.textContent).toContain('as viewer')
    expect(dialog()?.textContent).toContain("This link won't be shown again")
    await expect
      .poll(() => document.activeElement?.getAttribute('aria-label'))
      .toBe('Copy invite link')
    expect(sent('POST', /\/invitations$/)[0]?.body).toEqual({
      email: 'new@example.com',
      role: 'viewer',
    })
    // Not in any cache or storage while it shows (AC-17).
    const cached = JSON.stringify([
      queryClient
        .getQueryCache()
        .getAll()
        .map((query) => query.state.data),
      queryClient
        .getMutationCache()
        .getAll()
        .map((mutation) => mutation.state.data),
    ])
    expect(cached).not.toContain(fakeInviteUrl)
    const stored = [localStorage, sessionStorage].flatMap((storage) =>
      Object.keys(storage).map((key) => storage.getItem(key)),
    )
    expect(JSON.stringify(stored)).not.toContain(fakeInviteUrl)
    expect(window.location.href).not.toContain(fakeInviteUrl)

    await userEvent.click(button('Done') ?? document.body)
    await expect.poll(dialog).toBeNull()
    expect(document.body.innerHTML).not.toContain(fakeInviteUrl)
  })

  it('shows already_member under Email and other refusals in the alert, keeping the dialog open', async () => {
    const { screen } = await openMembers('owner')
    await userEvent.click(button('Invite') ?? document.body)
    await screen.getByLabelText('Email').fill('grace@example.com')
    api.failNext('POST', /\/invitations$/, 409, 'already_member', 'Already a member.')
    await userEvent.click(dialogButton('Invite') ?? document.body)
    await expect
      .poll(() => document.getElementById('invite-email-error')?.textContent)
      .toBe('Already a member.')

    await screen.getByLabelText('Email').fill('someone@example.com')
    api.failNext('POST', /\/invitations$/, 409, 'invitation_limit', 'Too many invitations.')
    await userEvent.click(dialogButton('Invite') ?? document.body)
    await expect
      .poll(() => dialog()?.querySelector('[role=alert]')?.textContent)
      .toContain('Too many invitations.')
    expect(dialog()?.textContent).toContain('Invite a teammate')
  })
})

describe('pending invitations (AC-18)', () => {
  it('lists invitations with who invited, the expiry, and Deleted account', async () => {
    await openMembers('owner')
    await expect.poll(text).toContain('new@example.com')
    expect(rowOf('new@example.com')?.textContent).toContain('Ada')
    expect(rowOf('old@example.com')?.textContent).toContain('Expired')
    expect(rowOf('old@example.com')?.textContent).toContain('Deleted account')
  })

  it('resends straight to the link step', async () => {
    await openMembers('owner')
    await expect.poll(text).toContain('old@example.com')
    await userEvent.click(button('Resend the invite to old@example.com') ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Share this invite link')
    expect(sent('POST', /\/invitations$/)[0]?.body).toEqual({
      email: 'old@example.com',
      role: 'viewer',
    })
  })

  it('revokes after a confirm that starts on Cancel', async () => {
    await openMembers('owner')
    await expect.poll(text).toContain('new@example.com')
    await userEvent.click(button('Revoke the invite to new@example.com') ?? document.body)
    await expect.poll(alertDialog).not.toBeNull()
    expect(document.activeElement?.textContent).toBe('Cancel')
    await userEvent.click(button('Revoke invite') ?? document.body)
    await expect.poll(() => rowOf('new@example.com')).toBeUndefined()
    expect(sent('DELETE', /\/invitations\/invm0000000000000001$/)).toHaveLength(1)
  })

  it('says "No pending invitations" when there are none', async () => {
    seed('owner')
    api.invitations = {}
    await renderApp(`/orgs/${orgId}/members`)
    await expect.poll(text).toContain('No pending invitations')
  })
})

describe('row menus (AC-19)', () => {
  it('gives owners Change role and Remove on others, and Change role and Leave on their own row', async () => {
    await openMembers('owner')
    await openRowMenu('Grace Hopper')
    expect(
      [...document.querySelectorAll('[role=menuitem]')].map((item) => item.textContent),
    ).toEqual(['Change role', 'Remove'])
    await userEvent.keyboard('{Escape}')
    await expect.poll(() => document.querySelector('[role=menu]')).toBeNull()
    await openRowMenu('Ada')
    expect(
      [...document.querySelectorAll('[role=menuitem]')].map((item) => item.textContent),
    ).toEqual(['Change role', 'Leave org'])
  })

  it('gives developers only Leave, on their own row', async () => {
    await openMembers('developer')
    expect(button('Actions for Grace Hopper')).toBeUndefined()
    await openRowMenu('Ada')
    expect(
      [...document.querySelectorAll('[role=menuitem]')].map((item) => item.textContent),
    ).toEqual(['Leave org'])
  })

  it('changes a role once it differs', async () => {
    const { screen } = await openMembers('owner')
    await openRowMenu('Grace Hopper')
    await chooseMenuItem('Change role')
    await expect.poll(dialog).not.toBeNull()
    expect(button('Save')?.getAttribute('aria-disabled')).toBe('true')
    await screen.getByRole('radio', { name: 'Viewer' }).click()
    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(dialog).toBeNull()
    expect(sent('PATCH', new RegExp(`/members/${grace}$`))[0]?.body).toEqual({ role: 'viewer' })
    await expect.poll(() => rowOf('Grace Hopper')?.textContent).toContain('Viewer')
  })

  it('warns before you demote yourself, and shows last_owner in the dialog', async () => {
    const { screen } = await openMembers('owner')
    await openRowMenu('Ada')
    await chooseMenuItem('Change role')
    await screen.getByRole('radio', { name: 'Developer' }).click()
    expect(dialog()?.textContent).toContain("You'll lose owner rights in this org")
    api.failNext('PATCH', /\/members\//, 409, 'last_owner', 'An org needs at least one owner.')
    await userEvent.click(button('Save') ?? document.body)
    await expect
      .poll(() => dialog()?.querySelector('[role=alert]')?.textContent)
      .toContain('An org needs at least one owner.')
  })

  it('refreshes your org role after you change your own', async () => {
    const { screen } = await openMembers('owner')
    api.members[orgId].push(makeMember({ userId: 'userm000000000000004', role: 'owner' }))
    await openRowMenu('Ada')
    await chooseMenuItem('Change role')
    await screen.getByRole('radio', { name: 'Developer' }).click()
    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(() => reason(button('Invite'))).toBe('Owners only')
  })

  it('removes a member after a confirm that names them', async () => {
    await openMembers('owner')
    await openRowMenu('Grace Hopper')
    await chooseMenuItem('Remove')
    await expect.poll(alertDialog).not.toBeNull()
    expect(alertDialog()?.textContent).toContain('Remove Grace Hopper from Acme?')
    expect(alertDialog()?.textContent).toContain('API keys they created keep working')
    await userEvent.click(button('Remove member') ?? document.body)
    await expect.poll(() => rowOf('Grace Hopper')).toBeUndefined()
    expect(sent('DELETE', new RegExp(`/members/${grace}$`))).toHaveLength(1)
  })

  it('shows a toast and the real list when the member is already gone', async () => {
    await openMembers('owner')
    await openRowMenu('Grace Hopper')
    await chooseMenuItem('Remove')
    api.failNext('DELETE', /\/members\//, 404, 'not_found', 'No such member.')
    api.members[orgId] = api.members[orgId].filter((member) => member.userId !== grace)
    await userEvent.click(button('Remove member') ?? document.body)
    await expect.poll(text).toContain("Couldn't remove the member")
    await expect.poll(() => rowOf('Grace Hopper')).toBeUndefined()
    await expect.poll(alertDialog).toBeNull()
  })

  it('leaves the org and lands on your next org', async () => {
    const { router } = await openMembers('developer')
    await openRowMenu('Ada')
    await chooseMenuItem('Leave org')
    await expect.poll(() => alertDialog()?.textContent).toContain('Leave Acme?')
    await userEvent.click(button('Leave org') ?? document.body)
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${otherOrgId}`)
    await expect.poll(text).toContain('You left Acme')
    expect(sent('DELETE', new RegExp(`/members/${accountId}$`))).toHaveLength(1)
  })

  it('turns every change off while the org is being deleted', async () => {
    await openMembers('owner', 'deleting')
    await openRowMenu('Grace Hopper')
    const items = [...document.querySelectorAll('[role=menuitem]')]
    expect(items.every((item) => item.getAttribute('aria-disabled') === 'true')).toBe(true)
    expect(items[0]?.textContent).toContain('Restore the org first')
    await userEvent.keyboard('{Escape}')
    expect(button('Resend the invite to new@example.com')?.getAttribute('aria-disabled')).toBe(
      'true',
    )
  })
})

describe('accessibility (AC-26)', () => {
  it.each([
    ['dark', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'compact'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in the %s theme, %s density', async (theme, density) => {
    setMode(theme, density)
    await openMembers('owner')
    await expect.poll(text).toContain('new@example.com')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })

  it('has no axe violations with the invite link step open', async () => {
    const { screen } = await openMembers('owner')
    await userEvent.click(button('Invite') ?? document.body)
    await screen.getByLabelText('Email').fill('new@example.com')
    await userEvent.click(dialogButton('Invite') ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Share this invite link')
    await new Promise((resolve) => setTimeout(resolve, 300))
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
