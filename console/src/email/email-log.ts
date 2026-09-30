import type { EmailFailureCode, EmailStatus, EmailTemplateName } from '@orvano/console-client'

/** What the Log calls each email (spec 0009, Template variables). */
export const templateLabels: Record<EmailTemplateName, string> = {
  verification: 'Email verification',
  recovery: 'Password reset',
  magic_link: 'Magic link',
  email_code: 'Email code',
  console_invitation: 'Console invite',
}

/** The Status badge of each delivery state (spec 0009, AC-20). */
export const statusInfo: Record<
  EmailStatus,
  { label: string; tone: 'neutral' | 'success' | 'danger' }
> = {
  queued: { label: 'Queued', tone: 'neutral' },
  sent: { label: 'Sent', tone: 'success' },
  failed: { label: 'Failed', tone: 'danger' },
}

/** Why an email was not sent, in plain words (spec 0009, Screens). */
export const reasonMessages: Record<EmailFailureCode, string> = {
  smtp_unreachable: 'Couldn’t connect to the SMTP server.',
  smtp_tls_failed:
    'The secure connection failed. Check the Security setting and the server’s certificate.',
  smtp_auth_failed: 'The SMTP server refused the username or password.',
  smtp_rejected: 'The SMTP server refused this email.',
  smtp_timeout: 'The SMTP server didn’t answer in time.',
  smtp_host_not_allowed:
    'The SMTP host points to a private network address, which projects can’t use.',
  email_not_configured: 'No email server was set up when it was sent.',
  project_not_active: 'Not sent: the project was deleted.',
  email_expired: 'Not sent: it waited more than 30 minutes in the queue.',
  email_unreadable: 'Not sent: the server couldn’t decrypt it. The master key may have changed.',
}
