import { z } from 'zod'

import { OrvanoError } from '@orvano/console-client'
import type { SmtpSecurity, SmtpSettings, SmtpSettingsInput } from '@orvano/console-client'

/** The SMTP form's values, as typed. */
export interface SmtpValues {
  host: string
  port: string
  security: SmtpSecurity
  username: string
  password: string
  fromEmail: string
  fromName: string
  replyTo: string
}

/** The form's fields by name, the same names the API puts before the colon of a field error. */
export type SmtpField = keyof SmtpValues

const fields: readonly SmtpField[] = [
  'host',
  'port',
  'security',
  'username',
  'password',
  'fromEmail',
  'fromName',
  'replyTo',
]

/** The Security choices in order, with the port each usually goes with (spec 0009, Screens). */
export const securityChoices: readonly { value: SmtpSecurity; label: string; port: string }[] = [
  { value: 'starttls', label: 'STARTTLS (usually port 587)', port: '587' },
  { value: 'tls', label: 'TLS (usually port 465)', port: '465' },
  { value: 'none', label: 'None', port: '25' },
]

/** The port Security fills in while Port is still empty. */
export function usualPort(security: SmtpSecurity): string {
  return securityChoices.find((choice) => choice.value === security)?.port ?? ''
}

/** The form's starting values: the stored settings, or an empty form on STARTTLS. The password is never stored here. */
export function initialValues(settings: SmtpSettings | null): SmtpValues {
  return {
    host: settings?.host ?? '',
    port: settings === null ? '' : String(settings.port),
    security: settings?.security ?? 'starttls',
    username: settings?.username ?? '',
    password: '',
    fromEmail: settings?.fromEmail ?? '',
    fromName: settings?.fromName ?? '',
    replyTo: settings?.replyTo ?? '',
  }
}

/** True when any value differs from where the form started. */
export function isChanged(values: SmtpValues, initial: SmtpValues): boolean {
  return fields.some((field) => values[field] !== initial[field])
}

/**
 * What the form checks before it asks the server: only that the required fields are filled and the
 * port is a number in range. The server owns every other rule and names the field it refuses.
 */
export const smtpSchema = z.object({
  host: z.string().trim().min(1, 'Enter the SMTP host'),
  port: z
    .string()
    .trim()
    .regex(/^\d{1,5}$/, 'Enter a port from 1 to 65535')
    .refine((port) => Number(port) >= 1 && Number(port) <= 65535, 'Enter a port from 1 to 65535'),
  security: z.custom<SmtpSecurity>(),
  username: z.string(),
  password: z.string(),
  fromEmail: z.string().trim().min(1, 'Enter the address emails are sent from'),
  fromName: z.string(),
  replyTo: z.string(),
})

const blankToNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())

/** The request body: trimmed, with empty optional fields as null. An empty password means "keep the stored one". */
export function toInput(values: SmtpValues): SmtpSettingsInput {
  return {
    host: values.host.trim(),
    port: Number(values.port.trim()),
    security: values.security,
    username: values.username === '' ? null : values.username,
    password: values.password === '' ? null : values.password,
    fromEmail: values.fromEmail.trim(),
    fromName: blankToNull(values.fromName),
    replyTo: blankToNull(values.replyTo),
  }
}

/**
 * Where a failed save or test shows (spec 0009, AC-2 and AC-6). A 400 whose detail starts with a
 * field's name and a colon shows the rest under that field, and a host that is not allowed shows
 * under Host; everything else shows in the form's alert.
 */
export function placeError(error: unknown): { field: SmtpField; message: string } | null {
  if (!(error instanceof OrvanoError) || error.status !== 400) return null
  if (error.code === 'smtp_host_not_allowed') return { field: 'host', message: error.message }
  const colon = error.message.indexOf(': ')
  if (colon <= 0) return null
  const field = fields.find((name) => name === error.message.slice(0, colon))
  return field === undefined ? null : { field, message: error.message.slice(colon + 2) }
}
