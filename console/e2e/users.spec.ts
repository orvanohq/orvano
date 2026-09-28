import { expect, scenariosProject, test } from './fixtures.ts'

// Spec 0004 AC-22 and AC-29 against the real API: an owner finds, creates, blocks, and deletes a
// user from the Users page, and rotates the project's signing key in Settings.

test('an owner manages users from the Users page', async ({ signedIn: page }) => {
  const email = `e2e-${String(Date.now())}@example.com`
  await page.goto(`/projects/${scenariosProject}`)
  await page
    .getByRole('navigation', { name: 'Project navigation' })
    .getByRole('link', { name: 'Users' })
    .click()
  await expect(page.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible()

  await page.getByRole('button', { name: 'Create user' }).click()
  const dialog = page.getByRole('dialog', { name: 'Create a user' })
  await dialog.getByLabel('Email').fill(email)
  await dialog.getByLabel('Password').fill('correct horse battery')
  await dialog.getByRole('button', { name: 'Create user' }).click()
  await expect(dialog).toBeHidden()

  await page.getByLabel('Search by email').fill(email.slice(0, 12).toUpperCase())
  await page.getByRole('link', { name: email }).click()
  await expect(page.getByRole('heading', { level: 1, name: email })).toBeVisible()
  await expect(page.getByText('No active sessions')).toBeVisible()

  await page.getByRole('button', { name: 'Block', exact: true }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Block user' }).click()
  await expect(page.getByText('Blocked', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Unblock' }).click()
  await expect(page.getByText('Active', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Delete user' }).click()
  const confirm = page.getByRole('alertdialog')
  await confirm.getByLabel(/Type/).fill(email)
  await confirm.getByRole('button', { name: 'Delete user' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Users' })).toBeVisible()
})

test('an owner rotates the signing key in Settings', async ({ signedIn: page }) => {
  await page.goto(`/projects/${scenariosProject}/settings`)
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible()
  // Count only once the keys have loaded.
  await expect(page.getByText('Active', { exact: true })).toHaveCount(1)
  const retiringBefore = await page.getByText('Retiring', { exact: true }).count()

  await page.getByRole('button', { name: 'Rotate key' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Rotate key' }).click()

  await expect(page.getByText('Retiring', { exact: true })).toHaveCount(retiringBefore + 1)
  await expect(page.getByText('Active', { exact: true })).toHaveCount(1)
})
