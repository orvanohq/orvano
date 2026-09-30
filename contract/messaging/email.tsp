// Transactional email (spec 0009, row 9): a project's SMTP settings, the test email, and the email log. The
// install's own SMTP and email log are in platform/install.tsp. Templates arrive with the next slice of the spec.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("How the connection to the SMTP server is secured.")
enum SmtpSecurity {
  @doc("Connect in plain text, then upgrade with STARTTLS (usually port 587). The send fails if the server doesn't offer it.")
  starttls,

  @doc("TLS from the first byte (usually port 465).")
  tls,

  @doc("No encryption. Only allowed without a username.")
  none,
}

@doc("Whose SMTP settings a project sends through.")
enum SmtpSource {
  @doc("The project's own settings.")
  project,

  @doc("The settings the install admin set for the whole server.")
  install,

  @doc("No SMTP server is set up, so the project can't send email.")
  none,
}

@doc("SMTP settings to save or to test.")
model SmtpSettingsInput {
  @doc("The SMTP server: a host name, or an IPv4 or IPv6 address (no brackets). At most 253 characters.")
  @example("smtp.example.com")
  host: string;

  @doc("The port, 1 to 65535.")
  @example(587)
  port: int32;

  @doc("How the connection is secured.")
  security: SmtpSecurity;

  @doc("The username to sign in with; null for a server that needs none. Needs `starttls` or `tls`.")
  @example("apikey")
  username: string | null;

  @doc("""
    The password. Null keeps the stored one, which works only while the host, port, and username are the stored
    ones; change any of them and you must send the password again.
    """)
  password: string | null;

  @doc("The address emails are sent from.")
  @example("hello@example.com")
  fromEmail: string;

  @doc("The name shown beside the From address; null for none. At most 128 characters.")
  @example("Example")
  fromName: string | null;

  @doc("Where replies go; null to use the From address.")
  @example("support@example.com")
  replyTo: string | null;
}

@doc("Stored SMTP settings. The password is never returned.")
model SmtpSettings {
  @doc("The SMTP server.")
  @example("smtp.example.com")
  host: string;

  @doc("The port.")
  @example(587)
  port: int32;

  @doc("How the connection is secured.")
  security: SmtpSecurity;

  @doc("The username, if the server needs one.")
  @example("apikey")
  username: string | null;

  @doc("True when a password is stored.")
  @example(true)
  hasPassword: boolean;

  @doc("The address emails are sent from.")
  @example("hello@example.com")
  fromEmail: string;

  @doc("The name shown beside the From address.")
  @example("Example")
  fromName: string | null;

  @doc("Where replies go; null means the From address.")
  @example("support@example.com")
  replyTo: string | null;

  @doc("When the settings last changed.")
  updatedAt: utcDateTime;
}

@doc("Who an email is sent as.")
model EmailSender {
  @doc("The From address.")
  @example("orvano@example.com")
  email: string;

  @doc("The name shown beside it, if any.")
  @example("Orvano")
  name: string | null;
}

@doc("The SMTP settings a project sends email through.")
model ProjectSmtp {
  @doc("Whose settings the project uses right now.")
  source: SmtpSource;

  @doc("The project's own settings; null unless `source` is `project`.")
  settings: SmtpSettings | null;

  @doc("Who the install's own SMTP sends as, whenever the install has one; null when it has none.")
  installSender: EmailSender | null;
}

@doc("The result of a test email.")
model EmailTestResult {
  @doc("The address the test email went to: your own console email.")
  @example("grace@example.com")
  sentTo: string;
}

@doc("Which email was sent: one of the four auth templates, or the console's own invite.")
enum EmailTemplateName {
  @doc("Confirms a user owns their email address.")
  verification,

  @doc("Lets a user reset their password.")
  recovery,

  @doc("Signs a user in with a link.")
  magic_link,

  @doc("Signs a user in with a one time code.")
  email_code,

  @doc("Invites someone to an org in the console.")
  console_invitation,
}

@doc("Where an email is in its delivery.")
enum EmailStatus {
  @doc("Waiting to be sent, or being retried.")
  queued,

  @doc("The SMTP server accepted it.")
  sent,

  @doc("It was not sent; `errorCode` says why.")
  failed,
}

@doc("Why an email was not sent.")
enum EmailFailureCode {
  @doc("The SMTP server could not be reached.")
  smtp_unreachable,

  @doc("The secure connection to the SMTP server failed.")
  smtp_tls_failed,

  @doc("The SMTP server refused the username or password.")
  smtp_auth_failed,

  @doc("The SMTP server refused the email.")
  smtp_rejected,

  @doc("The SMTP server did not answer in time.")
  smtp_timeout,

  @doc("The project's SMTP host points to a private network address.")
  smtp_host_not_allowed,

  @doc("No SMTP server was set up when the email was sent.")
  email_not_configured,

  @doc("The project was deleted before the email was sent.")
  project_not_active,

  @doc("The email waited more than 30 minutes in the queue.")
  email_expired,

  @doc("The server could not decrypt the queued email.")
  email_unreadable,
}

@doc("One email in the log. The recipient is masked, and the content is never kept.")
model EmailLogEntry {
  @doc("The email's ID.")
  @example("01924f5e-7c1a-7a3b-9f10-2c4d5e6f7a8b")
  id: string;

  @doc("Which email it was.")
  template: EmailTemplateName;

  @doc("The recipient, masked: the first character, then `***`, then the domain.")
  @example("g***@example.com")
  recipient: string;

  @doc("Where it is in its delivery.")
  status: EmailStatus;

  @doc("Whose SMTP settings sent it; null until it is sent.")
  smtpSource: SmtpSource | null;

  @doc("How many times the server tried to send it.")
  @example(1)
  attempts: int32;

  @doc("Why it was not sent; null unless `status` is `failed`.")
  errorCode: EmailFailureCode | null;

  @doc("When it was queued.")
  createdAt: utcDateTime;

  @doc("When it was sent or failed; null while it is queued.")
  completedAt: utcDateTime | null;
}

@doc("One page of the email log, newest first.")
model EmailPage {
  @doc("The emails on this page.")
  items: EmailLogEntry[];

  @doc("The cursor of the next page; null on the last one.")
  nextCursor: string | null;
}

@route("/v1/console/project/emails")
@tag("console")
@useAuth(consoleSession)
interface ConsoleEmailsApi {
  @doc("Lists the emails of the project named by `X-Orvano-Project` from the last 30 days, newest first. Any member.")
  @operationId("consoleEmails.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleEmails")
  @get
  list(
    @doc("The `nextCursor` of the page before; omit for the first page.")
    @query
    cursor?: string,

    @doc("The page size, 1 to 100. Default 25.")
    @query
    limit?: int32,
  ): EmailPage | Problem;
}

@route("/v1/console/project/email/smtp")
@tag("console")
@useAuth(consoleSession)
interface ConsoleSmtpApi {
  @doc("Gets the SMTP settings of the project named by `X-Orvano-Project`, and whose settings it sends through.")
  @operationId("consoleSmtp.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleSmtp")
  @get
  get(): ProjectSmtp | Problem;

  @doc("""
    Saves the project's own SMTP settings, replacing any stored ones. Owners and developers. Fails with
    `smtp_host_not_allowed` when the host is, or resolves to, a private network address.
    """)
  @operationId("consoleSmtp.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleSmtp")
  @extension("x-orvano-idempotent", true)
  @put
  update(@body request: SmtpSettingsInput): SmtpSettings | Problem;

  @doc("""
    Deletes the project's own SMTP settings, so it sends through the install's, if any. Owners and developers.
    Answers 204 also when nothing is stored.
    """)
  @operationId("consoleSmtp.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleSmtp")
  @extension("x-orvano-idempotent", true)
  @delete
  delete(): NoContentResponse | Problem;

  @doc("""
    Sends a test email to your own console email through the given settings, saved or not, and stores nothing.
    Owners and developers. Waits at most 20 seconds and never retries; a failure answers `smtp_unreachable`,
    `smtp_tls_failed`, `smtp_auth_failed`, `smtp_rejected`, or `smtp_timeout`.
    """)
  @operationId("consoleSmtp.test")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleSmtp")
  @post
  @route("/test")
  test(@body request: SmtpSettingsInput): EmailTestResult | Problem;
}
