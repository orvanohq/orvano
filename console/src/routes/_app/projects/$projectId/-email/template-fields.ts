import { OrvanoError, type EmailTemplate, type EmailTemplateInput } from '@orvano/console-client'

/** The three parts of a template, as the editor holds them. An empty text part means "generate it". */
export interface TemplateValues {
  subject: string
  html: string
  text: string
}

export type TemplatePart = keyof TemplateValues

const parts: readonly TemplatePart[] = ['subject', 'html', 'text']

/** How long the editor waits after the last keystroke before it asks for a preview (spec 0009, AC-9). */
export const previewDelayMs = 500

/** The editor's starting values for a stored or default template. */
export function initialValues(template: EmailTemplate): TemplateValues {
  return { subject: template.subject, html: template.html, text: template.text ?? '' }
}

/** True once any part differs from what the editor loaded, so there is something to save. */
export function isChanged(values: TemplateValues, initial: TemplateValues): boolean {
  return parts.some((part) => values[part] !== initial[part])
}

/** The request body: a blank text part is null, which asks the server to derive it from the HTML. */
export function toInput(values: TemplateValues): EmailTemplateInput {
  return {
    subject: values.subject,
    html: values.html,
    text: values.text.trim() === '' ? null : values.text,
  }
}

/**
 * Where a refused template shows (spec 0009, AC-10). The server starts the detail of a 400 and of a
 * 422 `template_invalid` with the part's name and a colon; the rest shows under that part. Anything
 * else belongs to no part.
 */
export function placeError(error: unknown): { part: TemplatePart; message: string } | null {
  if (!(error instanceof OrvanoError) || (error.status !== 400 && error.status !== 422)) return null
  const colon = error.message.indexOf(': ')
  if (colon <= 0) return null
  const part = parts.find((name) => name === error.message.slice(0, colon))
  if (part === undefined) return null
  const rest = error.message.slice(colon + 2)
  // "line 4: unknown variable action_ur" reads better as a sentence.
  const line = /^line (\d+): (.*)$/s.exec(rest)
  const message = line === null ? rest : `Line ${line[1]}: ${line[2]}`
  return { part, message: message.charAt(0).toUpperCase() + message.slice(1) }
}
