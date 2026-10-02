import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

// Spec 0009, AC-8 to AC-12, AC-30, and AC-33: the Templates tab and the template editor.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'
const base = `/projects/${projectId}/email/templates`
const templatePath = /\/email\/templates\/recovery$/
const previewPath = /\/email\/templates\/recovery\/preview$/

function seed(role: OrgRole = 'developer') {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001', name: 'Shop' })]
  api.emailTemplates = {}
  api.emailConfigured = true
  api.requests = []
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.textContent === name || element.getAttribute('aria-label') === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const dialog = () => document.querySelector('[data-slot=alert-dialog-content]')
const frame = () => document.querySelector<HTMLIFrameElement>('iframe[title="Email preview"]')
const previewSubject = () => document.querySelector('[data-testid=preview-subject]')?.textContent
/** The editable part of a CodeMirror editor, inside its shadow root, by its accessible name. */
const editor = (label: string) =>
  [...document.querySelectorAll('[data-slot=code-editor]')]
    .map((host) =>
      host.shadowRoot?.querySelector<HTMLElement>(`.cm-content[aria-label="${label}"]`),
    )
    .find((content) => content !== null && content !== undefined)
const sent = (method: string, path: RegExp) =>
  api.requests.filter((request) => request.method === method && path.test(request.path))

/** Opens the editor of the recovery template and waits for its first preview. */
async function openEditor(role: OrgRole = 'developer') {
  seed(role)
  const app = await renderApp(`${base}/recovery`)
  await expect.poll(() => editor('HTML')).toBeDefined()
  if (role !== 'viewer') await expect.poll(previewSubject).toBe('Password reset for Shop')
  return app
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the Email Templates tab', () => {
  it('lists the four templates with a Default or Custom badge and when one was edited', async () => {
    seed('viewer')
    api.emailTemplates = {
      recovery: {
        subject: 'Reset',
        html: '<p>x</p>',
        text: null,
        updatedAt: '2026-05-30T10:00:00.000Z',
      },
    }
    const { router } = await renderApp(base)
    await expect.poll(text).toContain('Password reset')
    await expect.poll(() => document.title).toBe('Email templates · Shop · Orvano')

    const rows = [...document.querySelectorAll('main li')].filter((row) =>
      row.querySelector('[data-slot=badge]'),
    )
    expect(rows.map((row) => row.querySelector('a')?.textContent)).toEqual([
      'Email verification',
      'Password reset',
      'Magic link',
      'Email code',
    ])
    expect(rows.map((row) => row.querySelector('[data-slot=badge]')?.textContent)).toEqual([
      'Default',
      'Custom',
      'Default',
      'Default',
    ])
    expect(rows[0]?.textContent).toContain('Sent to confirm a user owns their email address.')
    expect(rows[1]?.querySelector('time')?.getAttribute('datetime')).toBe(
      '2026-05-30T10:00:00.000Z',
    )
    expect(rows[0]?.querySelector('time')).toBeNull()
    expect(
      document.querySelector('nav[aria-label="Email sections"] [aria-current=page]')?.textContent,
    ).toBe('Templates')

    await userEvent.click(rows[2]?.querySelector('a') ?? document.body)
    await expect.poll(() => router.state.location.pathname).toBe(`${base}/magic_link`)
    await expect.poll(() => document.title).toBe('Magic link template · Shop · Orvano')
  })
})

describe('the template editor', () => {
  it('shows the subject, both editors, the variables, and a sandboxed preview', async () => {
    await openEditor()
    expect(text()).toContain('Sent when a user asks to reset their password.')
    expect(document.querySelector('main [data-slot=badge]')?.textContent).toBe('Default')
    expect(document.querySelector<HTMLInputElement>('#template-subject')?.value).toBe(
      'Password reset for {{ project.name }}',
    )
    expect(editor('HTML')?.textContent).toContain('{{ action_url }}')
    expect(editor('Text')?.textContent).toContain('Password reset: {{ action_url }}')
    // AC-30: a visible label beside each editor, with the same words as its accessible name.
    const labels = [...document.querySelectorAll('[data-slot=field] > span')].map(
      (label) => label.textContent,
    )
    expect(labels).toEqual(['HTML', 'Text'])

    // AC-31: the frame page, in an opaque origin (no allow-same-origin), never the HTML itself.
    expect(frame()?.getAttribute('src')).toBe('/frames/email-preview.html')
    expect(frame()?.getAttribute('sandbox')).toBe(
      'allow-scripts allow-popups allow-popups-to-escape-sandbox',
    )
    expect(frame()?.hasAttribute('srcdoc')).toBe(false)
    expect(sent('POST', previewPath)[0]?.body).toEqual({
      subject: 'Password reset for {{ project.name }}',
      html: '<h1>Password reset</h1>\n<p>Hi {{ user.name }}, <a href="{{ action_url }}">Open</a></p>',
      text: 'Password reset: {{ action_url }}',
    })

    const variables = [
      ...document.querySelectorAll('section[aria-labelledby=template-variables] li'),
    ]
    expect(variables.map((item) => item.querySelector('code')?.textContent)).toEqual([
      '{{ project.name }}',
      '{{ user.email }}',
      '{{ user.name }}',
      '{{ action_url }}',
      '{{ expires_in_minutes }}',
    ])
    expect(variables[0]?.textContent).toContain('Sample: Shop')
    await userEvent.click(button('Copy action_url') ?? document.body)
    await expect.poll(() => navigator.clipboard.readText()).toBe('{{ action_url }}')
  })

  it('previews unsaved content half a second after typing stops, and saves it', async () => {
    const { screen } = await openEditor()
    expect(reason(button('Save'))).toBe('Nothing to save yet')
    expect(button('Reset to default')).toBeUndefined()
    const before = sent('POST', previewPath).length

    await screen.getByLabelText('Subject').fill('Reset it, {{ project.name }}')
    await userEvent.click(editor('Text') ?? document.body)
    await userEvent.keyboard('{ControlOrMeta>}a{/ControlOrMeta}{Backspace}')
    // Nothing is asked while the typing goes on.
    expect(sent('POST', previewPath).length).toBe(before)
    await expect.poll(previewSubject).toBe('Reset it, Shop')
    expect(sent('POST', previewPath).at(-1)?.body).toMatchObject({
      subject: 'Reset it, {{ project.name }}',
      text: null,
    })
    expect(sent('PUT', templatePath)).toEqual([])

    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(text).toContain('Template saved')
    expect(sent('PUT', templatePath)[0]?.body).toEqual({
      subject: 'Reset it, {{ project.name }}',
      html: '<h1>Password reset</h1>\n<p>Hi {{ user.name }}, <a href="{{ action_url }}">Open</a></p>',
      text: null,
    })
    await expect
      .poll(() => document.querySelector('main [data-slot=badge]')?.textContent)
      .toBe('Custom')
    expect(reason(button('Save'))).toBe('Nothing to save yet')
    expect(button('Reset to default')).toBeDefined()
  })

  it('sends a test with the unsaved content and says where it went', async () => {
    const { screen } = await openEditor()
    await screen.getByLabelText('Subject').fill('Try {{ project.name }}')
    await userEvent.click(button('Send test') ?? document.body)
    await expect.poll(text).toContain('Sent to ada@example.com. Check your inbox.')
    expect(sent('POST', /\/recovery\/test$/)[0]?.body).toMatchObject({
      subject: 'Try {{ project.name }}',
    })
    expect(sent('PUT', templatePath)).toEqual([])

    api.emailConfigured = false
    await userEvent.click(button('Send test') ?? document.body)
    await expect.poll(text).toContain("Couldn't send the test email")
    expect(document.querySelector('[role=alert]')?.textContent).toContain(
      'No email server is set up.',
    )
  })

  it('shows what the server refuses under the part it names, and announces it', async () => {
    const { screen } = await openEditor()
    await screen.getByLabelText('Subject').fill('Hi {{ user.emial }}')
    await expect
      .poll(() => document.querySelector('#template-subject-error')?.textContent)
      .toBe('Line 1: unknown variable user.emial')
    expect(document.querySelector('#template-subject-error')?.getAttribute('role')).toBe('alert')
    expect(document.querySelector('#template-subject')?.getAttribute('aria-invalid')).toBe('true')
    expect(text()).toContain('The preview is waiting')

    // Fixing it brings the preview back.
    await screen.getByLabelText('Subject').fill('Hi {{ user.email }}')
    await expect.poll(previewSubject).toBe('Hi ada@example.com')
    expect(document.querySelector('#template-subject-error')).toBeNull()

    // A refused save names the HTML part: the error sits under that editor.
    api.failNext('PUT', templatePath, 422, 'template_invalid', 'html: line 2: unknown filter raw')
    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(text).toContain('Line 2: unknown filter raw')
    // The code editor sets aria-invalid in an effect, a render after the error text shows.
    await expect.poll(() => editor('HTML')?.getAttribute('aria-invalid')).toBe('true')
    await expect.poll(() => editor('Text')?.getAttribute('aria-invalid')).toBe('false')
  })

  it('resets a custom template to the default after a confirmation that starts on Cancel', async () => {
    seed()
    api.emailTemplates = {
      recovery: {
        subject: 'Mine',
        html: '<p>mine</p>',
        text: null,
        updatedAt: '2026-05-30T10:00:00.000Z',
      },
    }
    await renderApp(`${base}/recovery`)
    await expect.poll(() => editor('HTML')?.textContent).toBe('<p>mine</p>')
    expect(document.querySelector('main [data-slot=badge]')?.textContent).toBe('Custom')

    await userEvent.click(button('Reset to default') ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Reset to the default template?')
    await expect.poll(() => document.activeElement?.textContent).toBe('Cancel')
    await userEvent.click(
      [...(dialog()?.querySelectorAll('button') ?? [])].find(
        (item) => item.textContent === 'Reset to default',
      ) ?? document.body,
    )

    await expect.poll(text).toContain('Template reset')
    expect(sent('DELETE', templatePath).length).toBe(1)
    await expect.poll(() => editor('HTML')?.textContent).toContain('<h1>Password reset</h1>')
    expect(document.querySelector<HTMLInputElement>('#template-subject')?.value).toBe(
      'Password reset for {{ project.name }}',
    )
    expect(document.querySelector('main [data-slot=badge]')?.textContent).toBe('Default')
    expect(button('Reset to default')).toBeUndefined()
  })

  it('asks before leaving with unsaved changes', async () => {
    const { screen, router } = await openEditor()
    const back = () =>
      [...document.querySelectorAll('main a')].find((link) => link.textContent === 'All templates')

    await screen.getByLabelText('Subject').fill('Changed')
    await userEvent.click(back() ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Leave without saving?')
    await expect.poll(() => document.activeElement?.textContent).toBe('Cancel')
    await userEvent.keyboard('{Enter}')
    await expect.poll(dialog).toBeNull()
    expect(router.state.location.pathname).toBe(`${base}/recovery`)
    expect(document.querySelector<HTMLInputElement>('#template-subject')?.value).toBe('Changed')

    await userEvent.click(back() ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Leave without saving?')
    await userEvent.click(
      [...(dialog()?.querySelectorAll('button') ?? [])].find(
        (item) => item.textContent === 'Leave without saving',
      ) ?? document.body,
    )
    await expect.poll(() => router.state.location.pathname).toBe(base)
  })

  it('leaves without asking when nothing changed', async () => {
    const { router } = await openEditor()
    await userEvent.click(
      [...document.querySelectorAll('main a')].find(
        (link) => link.textContent === 'All templates',
      ) ?? document.body,
    )
    await expect.poll(() => router.state.location.pathname).toBe(base)
    expect(dialog()).toBeNull()
  })

  it('is read only for a viewer, with no preview, Save, Reset, or Send test', async () => {
    seed('viewer')
    api.emailTemplates = {
      recovery: {
        subject: 'Mine',
        html: '<p>mine</p>',
        text: null,
        updatedAt: '2026-05-30T10:00:00.000Z',
      },
    }
    await renderApp(`${base}/recovery`)
    await expect.poll(() => editor('HTML')?.textContent).toBe('<p>mine</p>')

    expect(text()).toContain('Editing, previews, and test emails are for developers and owners.')
    expect(document.querySelector<HTMLInputElement>('#template-subject')?.readOnly).toBe(true)
    expect(editor('HTML')?.getAttribute('aria-readonly')).toBe('true')
    expect(text()).toContain('{{ action_url }}')
    expect(frame()).toBeNull()
    expect(button('Save')).toBeUndefined()
    expect(button('Send test')).toBeUndefined()
    expect(button('Reset to default')).toBeUndefined()

    await userEvent.click(editor('HTML') ?? document.body)
    await userEvent.keyboard('typed')
    expect(editor('HTML')?.textContent).toBe('<p>mine</p>')
    expect(sent('POST', previewPath)).toEqual([])
  })

  it('says so when the template is not one of the four', async () => {
    seed()
    await renderApp(`${base}/console_invitation`)
    await expect.poll(text).toContain('No such template')
    expect(
      [...document.querySelectorAll('main a')].some((link) => link.textContent === 'All templates'),
    ).toBe(true)
  })

  // AC-30: Tab indents inside an editor, and Escape then Tab leaves it.
  it('lets the keyboard leave an editor with Escape then Tab', async () => {
    await openEditor()
    const html = editor('HTML')
    await userEvent.click(html ?? document.body)
    const host =
      html?.getRootNode() instanceof ShadowRoot ? (html.getRootNode() as ShadowRoot).host : null
    expect(document.activeElement).toBe(host)

    await userEvent.keyboard('{Tab}')
    expect(document.activeElement).toBe(host)
    await userEvent.keyboard('{Escape}{Tab}')
    await expect.poll(() => document.activeElement).not.toBe(host)
    // The next stop is the Text editor.
    expect(
      document.activeElement?.shadowRoot?.querySelector('.cm-content')?.getAttribute('aria-label'),
    ).toBe('Text')
  })

  // AC-30: the frame page's own title and lang are checked in the end to end test, which can enter it.
  it.each([
    ['dark', 'compact'],
    ['light', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in %s, %s', async (theme, density) => {
    setMode(theme, density)
    const editorApp = await openEditor()
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])

    // The list, as a viewer sees it.
    await editorApp.screen.unmount()
    seed('viewer')
    await renderApp(base)
    await expect.poll(text).toContain('Email verification')
    const listResults = await axe.run(document.body)
    expect(listResults.violations.map((violation) => violation.id)).toEqual([])
  })
})

/**
 * The editor's side of the preview frame (AC-33). The frame page runs in an opaque origin, so this
 * test can't look inside it: the outer frame's window is a stub that records what is posted to it,
 * and the page's messages are dispatched with that stub as their source.
 */
describe('the preview frame handshake', () => {
  const posts: { message: unknown; target: string }[] = []
  const stub = {
    postMessage: (message: unknown, target: string) => {
      posts.push({ message, target })
    },
  }
  const real = Object.getOwnPropertyDescriptor(HTMLIFrameElement.prototype, 'contentWindow')

  beforeEach(() => {
    posts.length = 0
    Object.defineProperty(HTMLIFrameElement.prototype, 'contentWindow', {
      configurable: true,
      get(this: HTMLIFrameElement): unknown {
        return this.title === 'Email preview' ? stub : (real?.get?.call(this) as unknown)
      },
    })
  })
  afterEach(() => {
    if (real !== undefined)
      Object.defineProperty(HTMLIFrameElement.prototype, 'contentWindow', real)
  })

  /** The HTML the editor sent the frame page, in order. */
  const rendered = () =>
    posts.flatMap(({ message, target }) => {
      const data = message as { type?: unknown; html?: unknown }
      return data.type === 'orvano.email-preview.render' ? [{ html: data.html, target }] : []
    })
  /** A message from the frame page, or from `source` when given. */
  const fromFrame = (data: unknown, source: unknown = stub) => {
    const event = new MessageEvent('message', { data })
    Object.defineProperty(event, 'source', { value: source })
    window.dispatchEvent(event)
  }
  const ready = { type: 'orvano.email-preview.ready' }
  const firstHtml =
    '<h1>Password reset</h1>\n<p>Hi Ada, <a href="https://example.com/auth/confirm?token=sample">Open</a></p>'

  it('answers ready with the HTML, then posts only when the HTML changes', async () => {
    const { screen } = await openEditor()
    expect(rendered()).toEqual([])

    // Another window, or a message of another shape, gets nothing.
    fromFrame(ready, window)
    fromFrame({ type: 'axe.ping' })
    fromFrame('orvano.email-preview.ready')
    expect(rendered()).toEqual([])

    fromFrame(ready)
    expect(rendered()).toEqual([{ html: firstHtml, target: '*' }])

    // A change to the subject alone posts nothing.
    await screen.getByLabelText('Subject').fill('Reset it, {{ project.name }}')
    await expect.poll(previewSubject).toBe('Reset it, Shop')
    expect(rendered()).toHaveLength(1)

    // A change to the HTML posts the new HTML once.
    await userEvent.click(editor('HTML') ?? document.body)
    await userEvent.keyboard('{ControlOrMeta>}a{/ControlOrMeta}Plain words')
    await expect.poll(() => rendered().length).toBe(2)
    expect(rendered()[1]).toEqual({ html: 'Plain words', target: '*' })

    // A reload of the frame page says ready again, and gets the latest HTML again.
    fromFrame(ready)
    expect(rendered()).toEqual([
      { html: firstHtml, target: '*' },
      { html: 'Plain words', target: '*' },
      { html: 'Plain words', target: '*' },
    ])
  })

  it('keeps the same frame through an error and while the Text view shows', async () => {
    const { screen } = await openEditor()
    fromFrame(ready)
    const mounted = frame()

    // A 422: the last preview stays, dimmed, in the same frame, and nothing new is posted.
    await screen.getByLabelText('Subject').fill('Hi {{ user.emial }}')
    await expect.poll(text).toContain('The preview is waiting')
    expect(frame()).toBe(mounted)
    expect(previewSubject()).toBe('Password reset for Shop')
    expect(
      document.querySelector('section[aria-labelledby=template-preview] .opacity-60'),
    ).not.toBeNull()

    // The Text view hides the frame without unmounting it.
    await userEvent.click(screen.getByRole('tab', { name: 'Text' }))
    await expect.poll(() => frame()?.checkVisibility()).toBe(false)
    expect(frame()).toBe(mounted)
    await userEvent.click(screen.getByRole('tab', { name: 'HTML' }))
    expect(frame()).toBe(mounted)

    // Fixing the error brings the preview back, with no reload and no second post of the same HTML.
    await screen.getByLabelText('Subject').fill('Hi {{ user.email }}')
    await expect.poll(previewSubject).toBe('Hi ada@example.com')
    expect(frame()).toBe(mounted)
    expect(rendered()).toHaveLength(1)
  })

  it('says so when the frame page never answers, and the other views keep working', async () => {
    const { screen } = await openEditor()
    await expect
      .poll(text, { timeout: 7000 })
      .toContain("Couldn't load the previewReload the page to try again.")
    await screen.getByLabelText('Subject').fill('Still {{ project.name }}')
    await expect.poll(previewSubject).toBe('Still Shop')
    await userEvent.click(screen.getByRole('tab', { name: 'Text' }))
    await expect
      .poll(text)
      .toContain('Password reset: https://example.com/auth/confirm?token=sample')

    // A late ready still works, and the message goes away.
    fromFrame(ready)
    await expect.poll(text).not.toContain('Reload the page to try again.')
    expect(rendered()).toHaveLength(1)
  })
})
