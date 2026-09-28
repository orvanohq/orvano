import { expect, test } from './fixtures.ts'

// Spec 0007 AC-23 against the real API, through the UI only: create an org and a project, create a
// scoped API key and prove its secret works, register a web and an Android platform, see the org
// delete refused while the project is live, then delete the project and restore it from the toast.

test('an admin creates an org, a project, a key, and platforms', async ({
  signedIn: page,
  request,
  baseURL,
}) => {
  const stamp = String(Date.now())

  // Create org from the /orgs header (AC-1).
  await page.goto('/orgs')
  await page.getByRole('button', { name: 'Create org' }).click()
  const orgDialog = page.getByRole('dialog', { name: 'Create an org' })
  await orgDialog.getByLabel('Name', { exact: true }).fill(`E2E org ${stamp}`)
  await orgDialog.getByRole('button', { name: 'Create org' }).click()
  await expect(orgDialog).toBeHidden()
  await expect(page).toHaveURL(/\/orgs\/[^/]+$/)
  const orgId = /\/orgs\/([^/]+)$/.exec(page.url())?.[1] ?? ''
  await expect(page.getByRole('heading', { level: 1, name: 'Projects' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: `Switch org: E2E org ${stamp}` })).toBeVisible()

  // Create project from the org page, then wait for setup to finish (AC-6).
  await page.getByRole('button', { name: 'Create project' }).first().click()
  const projectDialog = page.getByRole('dialog', { name: 'Create a project' })
  await projectDialog.getByLabel('Name', { exact: true }).fill(`E2E project ${stamp}`)
  await projectDialog.getByRole('button', { name: 'Create project' }).click()
  await expect(page).toHaveURL(/\/projects\/[a-z0-9]{20}$/)
  const projectId = /\/projects\/([a-z0-9]{20})$/.exec(page.url())?.[1] ?? ''
  await expect(page.getByRole('heading', { level: 1, name: `E2E project ${stamp}` })).toBeVisible({
    timeout: 30_000,
  })

  // Create a users.read key and read its secret from the reveal step (AC-11, AC-13, AC-14).
  const nav = page.getByRole('navigation', { name: 'Project navigation' })
  await nav.getByRole('link', { name: 'API keys' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'API keys' })).toBeVisible()
  await expect(page.getByText('No API keys yet')).toBeVisible()
  await page.getByRole('button', { name: 'Create key' }).first().click()
  const keyDialog = page.getByRole('dialog', { name: 'Create an API key' })
  await keyDialog.getByLabel('Name', { exact: true }).fill('E2E server')
  await keyDialog.getByRole('checkbox', { name: 'Users read' }).click()
  await keyDialog.getByRole('button', { name: 'Create key' }).click()

  const reveal = page.getByRole('dialog', { name: 'Copy your API key' })
  await expect(reveal).toBeVisible()
  await expect(reveal.getByRole('button', { name: 'Copy API key' })).toBeFocused()
  const secret = (await reveal.getByLabel('API key', { exact: true }).textContent()) ?? ''
  expect(secret).toMatch(/^orv_sk_/)
  await page.keyboard.press('Escape')
  await expect(reveal).toBeVisible()
  await reveal.getByRole('checkbox', { name: "I've copied this key and stored it safely" }).click()
  await reveal.getByRole('button', { name: 'Done' }).click()
  await expect(reveal).toBeHidden()
  await expect(page.getByRole('cell', { name: 'E2E server', exact: true })).toBeVisible()
  await expect(page.getByText(secret)).toHaveCount(0)

  // The secret authenticates a real server call to the new project (AC-23).
  const users = await request.get(`${baseURL ?? ''}/v1/users`, {
    headers: { 'X-Orvano-Key': secret, 'X-Orvano-Project': projectId },
  })
  expect(users.status()).toBe(200)

  // A pasted web URL becomes its hostname, and an Android app ID is checked (AC-17 to AC-19).
  await nav.getByRole('link', { name: 'Platforms' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Platforms' })).toBeVisible()
  await page.getByRole('button', { name: 'Add platform' }).first().click()
  const webDialog = page.getByRole('dialog', { name: 'Add a platform' })
  await webDialog.getByLabel('Name', { exact: true }).fill('Local web')
  await webDialog.getByLabel('Hostname').fill('http://LocalHost:3000/login')
  await webDialog.getByLabel('Hostname').blur()
  await expect(webDialog.getByLabel('Hostname')).toHaveValue('localhost')
  await webDialog.getByRole('button', { name: 'Add platform' }).click()
  await expect(webDialog).toBeHidden()

  await page.getByRole('button', { name: 'Add platform' }).first().click()
  const androidDialog = page.getByRole('dialog', { name: 'Add a platform' })
  await androidDialog.getByRole('combobox', { name: 'Type' }).click()
  await page.getByRole('option', { name: 'Android' }).click()
  await androidDialog.getByLabel('Name', { exact: true }).fill('Android app')
  await androidDialog.getByLabel('Package name').fill('com.example.e2e')
  await androidDialog.getByRole('button', { name: 'Add platform' }).click()
  await expect(androidDialog).toBeHidden()

  await expect(page.getByRole('cell', { name: 'localhost', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'com.example.e2e', exact: true })).toBeVisible()

  // The overview's Connect your app steps are both done now (AC-21).
  await nav.getByRole('link', { name: 'Overview' }).click()
  const steps = page.getByRole('list').filter({ hasText: 'Add a platform' }).getByRole('listitem')
  await expect(steps.filter({ hasText: 'Done' })).toHaveCount(2)

  // The org can't be deleted while its project is live (AC-4).
  await page.getByRole('link', { name: `E2E org ${stamp}` }).click()
  await expect(page).toHaveURL(new RegExp(`/orgs/${orgId}$`))
  const orgNav = page.getByRole('navigation', { name: 'Org navigation' })
  await orgNav.getByRole('link', { name: 'Settings' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Settings' })).toBeVisible()
  const deleteOrg = page.getByRole('button', { name: 'Delete org' })
  await expect(deleteOrg).toHaveAccessibleDescription('Delete its projects first')
  await expect(deleteOrg).toHaveAttribute('aria-disabled', 'true')

  // Delete the project by typing its name, then restore it from the toast (AC-8, AC-9).
  await orgNav.getByRole('link', { name: 'Projects' }).click()
  await page.getByRole('link', { name: `E2E project ${stamp}` }).click()
  await nav.getByRole('link', { name: 'Settings' }).click()
  await page.getByRole('button', { name: 'Delete project' }).click()
  const confirm = page.getByRole('alertdialog', { name: `Delete E2E project ${stamp}?` })
  const confirmButton = confirm.getByRole('button', { name: 'Delete project' })
  await expect(confirmButton).toBeDisabled()
  await confirm.getByLabel(/to confirm/).fill(`E2E project ${stamp}`)
  await confirmButton.click()
  await expect(page).toHaveURL(new RegExp(`/orgs/${orgId}$`))
  const row = page.getByRole('row').filter({ hasText: `E2E project ${stamp}` })
  await expect(row).toContainText('Deleting')
  await page.getByRole('button', { name: 'Restore', exact: true }).click()
  await expect(page.getByText('Project restored')).toBeVisible()
  await expect(row).toContainText(/Setting up|Active/)
})
