import { expect, scenariosProject, test } from './fixtures.ts'

const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025'

interface MailpitSearch {
  messages: { ID: string }[]
}

// Spec 0010 AC-22 and AC-23 against the real API, through the UI only: an owner sees a new user as
// unverified, filters the list by it (kept in the URL), marks the user verified, and sends a password
// reset whose link URL the console remembers; the email arrives in Mailpit with the link.

test('an owner manages a user email and verification from the Users page', async ({
  signedIn: page,
  request,
}) => {
  const email = `e2e-verify-${String(Date.now())}@example.com`
  await page.goto(`/projects/${scenariosProject}/users`)
  await page.getByRole('button', { name: 'Create user' }).click()
  const create = page.getByRole('dialog', { name: 'Create a user' })
  await create.getByLabel('Email').fill(email)
  await create.getByLabel('Password').fill('correct horse battery')
  await create.getByRole('button', { name: 'Create user' }).click()
  await expect(create).toBeHidden()

  // The filter lives in the URL, and the new user shows as unverified.
  await page.getByLabel('Search by email').fill(email)
  await page.getByRole('combobox', { name: 'Verification' }).click()
  await page.getByRole('option', { name: 'Unverified' }).click()
  await expect(page).toHaveURL(/emailVerified=false/)
  const row = page.getByRole('row', { name: new RegExp(email) })
  await expect(row.getByText('Unverified')).toBeVisible()
  await row.getByRole('link', { name: email }).click()

  await expect(page.getByRole('heading', { level: 1, name: email })).toBeVisible()
  await expect(page.getByText('Not verified')).toBeVisible()
  await page.getByRole('button', { name: 'Mark as verified' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Mark as verified' }).click()
  await expect(page.getByText(/^Verified on /)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send verification email' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Send password reset email' }).click()
  const send = page.getByRole('dialog', { name: 'Send password reset email' })
  await send.getByLabel('Link opens at').fill('http://localhost:3000/auth/callback')
  await send.getByRole('button', { name: 'Send email' }).click()
  await expect(send).toBeHidden()
  await expect(page.getByText(`Sent to ${email}.`)).toBeVisible()

  // The same URL comes back next time.
  await page.getByRole('button', { name: 'Send password reset email' }).click()
  await expect(send.getByLabel('Link opens at')).toHaveValue('http://localhost:3000/auth/callback')
  await send.getByRole('button', { name: 'Cancel' }).click()

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
    Text: string
  }
  expect(message.Text).toContain(
    'http://localhost:3000/auth/callback?orvano_type=recovery&orvano_token=orv_el_',
  )
})
