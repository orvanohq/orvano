// Install settings (spec 0003): the console sign up policy, changed by install admins only.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";

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

@route("/v1/console/install/settings")
@tag("console")
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
