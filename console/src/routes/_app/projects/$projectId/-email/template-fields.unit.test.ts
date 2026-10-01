import { describe, expect, it } from 'vitest'

import { OrvanoError, type EmailTemplate } from '@orvano/console-client'

import { initialValues, isChanged, placeError, toInput } from './template-fields.ts'

// Spec 0009, AC-9 and AC-10: what the template editor sends, and where a refusal shows.

const template: EmailTemplate = {
  kind: 'recovery',
  locale: 'en',
  subject: 'Reset your password for {{ project.name }}',
  html: '<p>{{ action_url }}</p>',
  text: null,
  isCustom: false,
  updatedAt: null,
  variables: [],
}

const problem = (status: number, code: string, detail: string) =>
  new OrvanoError(status, code, detail, 'req_1')

describe('the template editor values', () => {
  it('start from the template, with no text part as an empty one', () => {
    expect(initialValues(template)).toEqual({
      subject: template.subject,
      html: template.html,
      text: '',
    })
    expect(initialValues({ ...template, text: 'Plain' }).text).toBe('Plain')
  })

  it('count as changed once any part differs', () => {
    const initial = initialValues(template)
    expect(isChanged(initial, initial)).toBe(false)
    expect(isChanged({ ...initial, subject: 'Reset' }, initial)).toBe(true)
    expect(isChanged({ ...initial, html: '<p></p>' }, initial)).toBe(true)
    expect(isChanged({ ...initial, text: 'Plain' }, initial)).toBe(true)
  })

  it('send a blank text part as null, and nothing else changed', () => {
    const initial = initialValues(template)
    expect(toInput(initial)).toEqual({ subject: template.subject, html: template.html, text: null })
    expect(toInput({ ...initial, text: ' \n ' }).text).toBeNull()
    expect(toInput({ ...initial, text: ' Plain ' }).text).toBe(' Plain ')
  })
})

describe('a refused template', () => {
  it('shows under the part the server names, with the line first', () => {
    expect(
      placeError(problem(422, 'template_invalid', 'html: line 4: unknown variable action_ur')),
    ).toEqual({ part: 'html', message: 'Line 4: unknown variable action_ur' })
    expect(
      placeError(problem(422, 'template_invalid', 'text: rendering takes more than 100,000 steps')),
    ).toEqual({ part: 'text', message: 'Rendering takes more than 100,000 steps' })
    expect(placeError(problem(400, 'invalid_request', 'subject: Enter a subject.'))).toEqual({
      part: 'subject',
      message: 'Enter a subject.',
    })
  })

  it('belongs to no part when the server names none', () => {
    expect(placeError(problem(409, 'email_not_configured', 'html: no'))).toBeNull()
    expect(placeError(problem(502, 'smtp_unreachable', "Couldn't connect."))).toBeNull()
    expect(placeError(problem(400, 'invalid_request', 'Send the project ID.'))).toBeNull()
    expect(placeError(problem(400, 'invalid_request', 'host: Enter the host.'))).toBeNull()
    expect(placeError(new Error('html: offline'))).toBeNull()
  })
})
