import { expect, scenariosProject, test } from './fixtures.ts'

// Spec 0014 AC-32 and AC-35 against the real API, through the UI: the owner saves the Anonymous
// users card (guests are on in the fixture project), and finds a guest by the Users page's Kind
// filter, marked Guest in the list and on their page.

test('an owner saves the guest settings and finds a guest on the Users page', async ({
  signedIn: page,
  request,
  baseURL,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')

  await page.goto(`/projects/${scenariosProject}/sign-in-methods`)
  const card = page.getByRole('form', { name: 'Anonymous users' })
  await expect(card.getByRole('switch', { name: 'Allow guest sign in' })).toBeChecked()
  const days = card.getByRole('textbox', { name: 'Delete idle guests after' })
  try {
    await days.fill('45')
    await card.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Anonymous users saved')).toBeVisible()
    await expect(days).toHaveValue('45')
  } finally {
    // Other tests and scenarios expect the default.
    await days.fill('30')
    await card.getByRole('button', { name: 'Save' }).click()
    await expect(days).toHaveValue('30')
  }

  const guest = await request.post(`${baseURL}/v1/account/sessions/anonymous`, {
    headers: { 'X-Orvano-Project': scenariosProject },
  })
  expect(guest.status()).toBe(201)
  const userId = ((await guest.json()) as { user: { id: string } }).user.id

  await page.goto(`/projects/${scenariosProject}/users?anonymous=true`)
  const row = page.getByRole('row').filter({ hasText: userId })
  await expect(row.getByText('Guest')).toBeVisible()
  await row.getByRole('link', { name: userId }).click()
  await expect(page.getByText('This user is a guest', { exact: false })).toBeVisible()
})
