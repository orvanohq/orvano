import { OrvanoError } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { SetupForm, SignInForm } from './auth-form'

// Spec 0004 AC-27 and spec 0006 AC-23: the sign in and first admin forms check their fields, show
// Orvano's refusal in words, and meet WCAG AA.

describe('SignInForm', () => {
  it('sends the email and password, and shows a wrong password as words, not JSON', async () => {
    const onSubmit = vi
      .fn()
      .mockRejectedValue(new OrvanoError(401, 'invalid_credentials', 'raw', null))
    const screen = await render(
      <main>
        <h1 id="page-title">Sign in</h1>
        <SignInForm onSubmit={onSubmit} />
      </main>,
    )

    await screen.getByLabelText('Email').fill('ada@example.com')
    await screen.getByLabelText('Password').fill('correct horse battery')
    await screen.getByRole('button', { name: 'Sign in' }).click()

    expect(onSubmit).toHaveBeenCalledWith({
      email: 'ada@example.com',
      password: 'correct horse battery',
    })
    await expect
      .element(screen.getByRole('alert').getByText('The email or password is wrong.'))
      .toBeVisible()
  })

  it('checks the fields before sending anything', async () => {
    const onSubmit = vi.fn()
    const screen = await render(<SignInForm onSubmit={onSubmit} />)

    await screen.getByRole('button', { name: 'Sign in' }).click()

    await expect.element(screen.getByText('Enter your email.')).toBeVisible()
    await expect.element(screen.getByText('Enter your password.')).toBeVisible()
    expect(onSubmit).not.toHaveBeenCalled()
    await expect.element(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
  })

  it('is usable from the keyboard alone', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    await render(<SignInForm onSubmit={onSubmit} />)

    await userEvent.tab()
    await userEvent.keyboard('ada@example.com')
    await userEvent.tab()
    await userEvent.keyboard('correct horse battery{Enter}')

    await expect.poll(() => onSubmit.mock.calls.length).toBe(1)
  })

  it('has no axe violations, with an error showing', async () => {
    const screen = await render(
      <main>
        <h1 id="page-title">Sign in</h1>
        <SignInForm
          onSubmit={() => Promise.reject(new OrvanoError(429, 'rate_limited', 'raw', null))}
        />
      </main>,
    )
    await screen.getByRole('button', { name: 'Sign in' }).click()
    await screen.getByLabelText('Email').fill('ada@example.com')
    await screen.getByLabelText('Password').fill('x')
    await screen.getByRole('button', { name: 'Sign in' }).click()
    await expect.element(screen.getByRole('alert').getByText(/Too many attempts/)).toBeVisible()

    const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})

describe('SetupForm', () => {
  it('asks for a new password of at least 8 characters, and shows an invalid setup link in words', async () => {
    const onSubmit = vi
      .fn()
      .mockRejectedValue(new OrvanoError(403, 'setup_token_invalid', 'raw', null))
    const screen = await render(<SetupForm onSubmit={onSubmit} />)

    await screen.getByLabelText('Email').fill('ada@example.com')
    await screen.getByLabelText('Password').fill('short')
    await screen.getByRole('button', { name: 'Create the first admin' }).click()
    await expect.element(screen.getByText('Use at least 8 characters.')).toBeVisible()
    expect(onSubmit).not.toHaveBeenCalled()

    await screen.getByLabelText('Name').fill('Ada')
    await screen.getByLabelText('Password').fill('correct horse battery')
    await screen.getByRole('button', { name: 'Create the first admin' }).click()

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Ada',
      email: 'ada@example.com',
      password: 'correct horse battery',
    })
    await expect
      .element(
        screen
          .getByRole('alert')
          .getByText(
            'This setup link is not valid. Run the installer again on your server to see the right link.',
          ),
      )
      .toBeVisible()
  })

  it('has no axe violations', async () => {
    await render(
      <main>
        <h1 id="page-title">Create the first admin</h1>
        <SetupForm onSubmit={() => Promise.resolve()} />
      </main>,
    )

    const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
