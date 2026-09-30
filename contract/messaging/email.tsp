// Transactional email (spec 0009, row 9): a project's SMTP settings and the test email. Templates, the email
// log, and the install's own SMTP arrive with the later slices of the same spec.
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
