import { expect, fixturesOrgId, scenariosProject, test } from './fixtures.ts'

const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025'

interface MailpitSearch {
  messages: { ID: string }[]
}

// Spec 0008 AC-27 against the real API, through the UI only: the fixture owner invites a new email as
// developer and reads the link from the link step; the same link arrives by email through the
// install's SMTP (Mailpit) and shows as sent in the console's email log (spec 0009, AC-21, AC-23);
// a second browser opens it, creates an account,
// and lands in the org; the owner makes that member a viewer, who then can't create keys; the owner
// removes them, and their next visit to the org shows the not found screen.

test('an owner invites a teammate, changes their role, and removes them', async ({
  signedIn: owner,
  browser,
  request,
  baseURL,
  session,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  const orgId = await fixturesOrgId(request, baseURL, session)
  const local = `e2e-member-${String(Date.now())}`
  const email = `${local}@example.com`

  // Invite as developer and read the link from the link step (AC-16, AC-17).
  await owner.goto(`/orgs/${orgId}/members`)
  await expect(owner.getByRole('heading', { level: 1, name: 'Members' })).toBeVisible()
  await owner.getByRole('button', { name: 'Invite', exact: true }).click()
  const inviteDialog = owner.getByRole('dialog', { name: 'Invite a teammate' })
  await inviteDialog.getByLabel('Email').fill(email)
  await expect(inviteDialog.getByRole('radio', { name: 'Developer' })).toBeChecked()
  await inviteDialog.getByRole('button', { name: 'Invite', exact: true }).click()
  const linkStep = owner.getByRole('dialog', { name: 'Share this invite link' })
  await expect(linkStep).toBeVisible()
  await expect(linkStep).toContainText(`We’re sending the invite to ${email}.`)
  await expect(linkStep.getByRole('button', { name: 'Copy invite link' })).toBeFocused()
  const url = (await linkStep.getByLabel('invite link', { exact: true }).textContent()) ?? ''
  expect(url).toMatch(/\/invite#[A-Za-z0-9_-]{43}$/)
  await linkStep.getByRole('button', { name: 'Done' }).click()
  await expect(linkStep).toBeHidden()
  await expect(owner.getByText(url)).toHaveCount(0)
  await expect(owner.getByRole('cell', { name: email, exact: true })).toBeVisible()

  // The install has SMTP, so the same link also arrives by email (spec 0009, AC-23). The worker
  // sends it from the queue, which can take a while right after the stack starts.
  let id = ''
  await expect
    .poll(
      async () => {
        const search = await request.get(
          `${mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
        )
        id = ((await search.json()) as MailpitSearch).messages[0]?.ID ?? ''
        return id
      },
      { timeout: 20_000 },
    )
    .not.toBe('')
  const message = (await (await request.get(`${mailpit}/api/v1/message/${id}`)).json()) as {
    Subject: string
    Text: string
    HTML: string
  }
  expect(message.Subject).toBe('Fixture Admin invited you to join Fixtures on Orvano')
  expect(message.Text).toContain(url)
  expect(message.HTML).toContain(`href="${url}"`)

  // The console's email log shows it sent, with the address masked (spec 0009, AC-21, AC-22).
  await owner.goto('/install')
  const newest = owner.getByRole('region', { name: 'Console emails' }).locator('tbody tr').first()
  await expect(async () => {
    await owner.reload()
    await expect(newest).toContainText('Sent', { timeout: 1000 })
  }).toPass()
  await expect(newest).toContainText('Console invite')
  await expect(newest).toContainText('e***@example.com')
  await expect(owner.getByText(email)).toHaveCount(0)
  await owner.goto(`/orgs/${orgId}/members`)

  // The teammate opens the link in their own browser and creates an account (AC-20, AC-22). The
  // link carries the API's public URL; the console is served here, so keep only its fragment.
  const teammateContext = await browser.newContext({ baseURL })
  const teammate = await teammateContext.newPage()
  await teammate.goto(`/invite${url.slice(url.indexOf('#'))}`)
  await expect(teammate.getByText(/invited you to join Fixtures as developer/)).toBeVisible()
  expect(new URL(teammate.url()).hash).toBe('')
  const createTab = teammate.getByRole('tabpanel', { name: 'Create account' })
  await expect(createTab.getByLabel('Email')).toHaveValue(email)
  await createTab.getByLabel('Password').fill('e2e member password')
  await createTab.getByRole('button', { name: 'Create account' }).click()
  await expect(teammate).toHaveURL(new RegExp(`/orgs/${orgId}$`))
  await expect(teammate.getByText('You joined Fixtures')).toBeVisible()

  // The owner makes them a viewer (AC-19).
  await owner.reload()
  await owner.getByRole('button', { name: `Actions for ${local}` }).click()
  await owner.getByRole('menuitem', { name: 'Change role' }).click()
  const roleDialog = owner.getByRole('dialog', { name: 'Change role' })
  await roleDialog.getByRole('radio', { name: 'Viewer' }).click()
  await roleDialog.getByRole('button', { name: 'Save' }).click()
  await expect(roleDialog).toBeHidden()
  await expect(owner.getByRole('row', { name: new RegExp(local) })).toContainText('Viewer')

  // The viewer can't create keys (spec 0007, AC-22).
  await teammate.goto(`/projects/${scenariosProject}/keys`)
  const createKey = teammate.getByRole('button', { name: 'Create key' }).first()
  await expect(createKey).toHaveAttribute('aria-disabled', 'true')
  await expect(createKey).toHaveAccessibleDescription('Developers and owners only')

  // The owner removes them; their next visit to the org shows the not found screen (AC-10).
  await owner.getByRole('button', { name: `Actions for ${local}` }).click()
  await owner.getByRole('menuitem', { name: 'Remove' }).click()
  const confirm = owner.getByRole('alertdialog')
  await expect(confirm).toContainText(`Remove ${local} from Fixtures?`)
  await confirm.getByRole('button', { name: 'Remove member' }).click()
  // The confirm closes once the DELETE has finished; while it is open the table is aria-hidden, so
  // the row count would reach 0 early and the teammate could visit before the removal commits.
  await expect(confirm).toBeHidden()
  await expect(owner.getByRole('row', { name: new RegExp(local) })).toHaveCount(0)

  await teammate.goto(`/orgs/${orgId}`)
  await expect(teammate.getByRole('heading', { level: 1, name: 'Org not found' })).toBeVisible()
  await teammateContext.close()
})
