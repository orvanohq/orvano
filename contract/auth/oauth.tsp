// The provider callback (spec 0012, AC-5): where Google, Apple, GitHub, and Microsoft send the user's browser back.
// Audience `browser`: a person's browser reaches it by navigation and no SDK calls it, so SdkGen generates nothing
// for it. It answers a redirect to the app, or a small static HTML page when the flow is unknown.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("The provider sent the browser back: on to the app's redirect URL with `orvano_code` or `orvano_error`.")
model OAuthCallbackRedirect {
  @statusCode statusCode: 302 | 303;

  @doc("The app's redirect URL with `orvano_type` and `orvano_code` or `orvano_error`.")
  @header location: string;
}

@doc("The flow is unknown, used, or expired (400), or the callback limit was reached (429): a static page saying to go back to the app.")
model OAuthCallbackPage {
  @statusCode statusCode: 400 | 429;
  @header contentType: "text/html";

  @doc("The page.")
  @body body: string;
}

@route("/v1/projects/{projectId}/oauth/{provider}/callback")
@tag("oauth")
interface OAuthCallbackApi {
  @doc("The provider's redirect back, with a query (Google, GitHub, Microsoft).")
  @operationId("oauth.callback")
  @extension("x-orvano-audience", "browser")
  @extension("x-orvano-service", "oauth")
  @get
  callback(
    @doc("The project the flow belongs to.") @path projectId: string,
    @doc("The provider the flow belongs to.") @path provider: string,
    @doc("The flow's state.") @query state?: string,
    @doc("The provider's authorization code.") @query code?: string,
    @doc("The provider's error, such as `access_denied`.") @query error?: string,
  ): OAuthCallbackRedirect | OAuthCallbackPage | Problem;

  @doc("The provider's redirect back as a form post (Apple).")
  @operationId("oauth.callbackForm")
  @extension("x-orvano-audience", "browser")
  @extension("x-orvano-service", "oauth")
  @post
  callbackForm(
    @doc("The project the flow belongs to.") @path projectId: string,
    @doc("The provider the flow belongs to.") @path provider: string,
    @header contentType: "application/x-www-form-urlencoded",
    @body form: OAuthCallbackForm,
  ): OAuthCallbackRedirect | OAuthCallbackPage | Problem;
}

@doc("Apple's form post.")
model OAuthCallbackForm {
  @doc("The flow's state.")
  state?: string;

  @doc("The provider's authorization code.")
  code?: string;

  @doc("Apple's JSON with the user's name, sent only on the first authorization.")
  user?: string;

  @doc("The provider's error, such as `user_cancelled_authorize`.")
  error?: string;
}
