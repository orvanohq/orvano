// Install settings (spec 0003): the console sign up policy, changed by install admins only. And the
// setup status (spec 0006): whether the install still waits for its first admin.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";

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
