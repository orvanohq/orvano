// Install settings (spec 0003): the console sign up policy, changed by install admins only. And the
// setup status (spec 0006): whether the install still waits for its first admin. And the install's own
// SMTP server and email log (spec 0009), which the Messaging module serves.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "../messaging/email.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Who may create a console account.")
enum ConsoleSignupMode {
  @doc("Only people with an invitation (the first account on an install is always admitted).")
  invite,

  @doc("Anyone who can reach the console.")
  open,
}

@doc("Settings for the whole install.")
model InstallSettings {
  @doc("Who may create a console account.")
  consoleSignup: ConsoleSignupMode;

  @doc("When the settings last changed.")
  updatedAt: utcDateTime;
}

@doc("Changes to the install settings.")
model UpdateInstallSettingsRequest {
  @doc("Who may create a console account.")
  consoleSignup: ConsoleSignupMode;
}

@doc("Whether the install still waits for its first admin.")
model InstallSetup {
  @doc("True while no install admin exists: the first console account must come from the installer's setup link.")
  @example(true)
  setupRequired: boolean;

  @doc("True when anyone who can reach the console may create an account (`consoleSignup` is `open`).")
  @example(false)
  signupOpen: boolean;
}

@route("/v1/console/install/settings")
@tag("console")
@useAuth(consoleSession)
interface ConsoleInstallApi {
  @doc("Gets the install settings. Install admins only.")
  @operationId("consoleInstall.getSettings")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @get
  getSettings(): InstallSettings | Problem;

  @doc("Changes the install settings. Install admins only.")
  @operationId("consoleInstall.updateSettings")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @patch(#{ implicitOptionality: false })
  updateSettings(@body request: UpdateInstallSettingsRequest): InstallSettings | Problem;
}

@doc("The SMTP server of the whole install, which every project without its own sends through.")
model InstallSmtp {
  @doc("The install's settings; null when none are set.")
  settings: SmtpSettings | null;
}

@route("/v1/console/install/smtp")
@tag("console")
@useAuth(consoleSession)
interface ConsoleInstallSmtpApi {
  @doc("Gets the install's SMTP settings. Install admins only.")
  @operationId("consoleInstall.getSmtp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @get
  getSmtp(): InstallSmtp | Problem;

  @doc("""
    Saves the install's SMTP settings, replacing any stored ones. Install admins only. A private network address
    is allowed here, since the install admin already controls the server.
    """)
  @operationId("consoleInstall.updateSmtp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @extension("x-orvano-idempotent", true)
  @put
  updateSmtp(@body request: SmtpSettingsInput): SmtpSettings | Problem;

  @doc("""
    Deletes the install's SMTP settings, so projects without their own can't send email. Install admins only.
    Answers 204 also when nothing is stored.
    """)
  @operationId("consoleInstall.deleteSmtp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @extension("x-orvano-idempotent", true)
  @delete
  deleteSmtp(): NoContentResponse | Problem;

  @doc("""
    Sends a test email to your own console email through the given settings, saved or not, and stores nothing.
    Install admins only. Waits at most 20 seconds and never retries.
    """)
  @operationId("consoleInstall.testSmtp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @post
  @route("/test")
  testSmtp(@body request: SmtpSettingsInput): EmailTestResult | Problem;
}

@route("/v1/console/install/emails")
@tag("console")
@useAuth(consoleSession)
interface ConsoleInstallEmailsApi {
  @doc("Lists the console's own emails (invites) from the last 30 days, newest first. Install admins only.")
  @operationId("consoleInstall.listEmails")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @get
  listEmails(
    @doc("The `nextCursor` of the page before; omit for the first page.")
    @query
    cursor?: string,

    @doc("The page size, 1 to 100. Default 25.")
    @query
    limit?: int32,
  ): EmailPage | Problem;
}

@route("/v1/console/install/setup")
@tag("console")
interface ConsoleInstallSetupApi {
  @doc("""
    Tells whether the install still waits for its first admin. Needs no console session, so the console can
    send you to the setup screen before anyone can sign in.
    """)
  @operationId("consoleInstall.getSetup")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInstall")
  @get
  getSetup(): InstallSetup | Problem;
}
