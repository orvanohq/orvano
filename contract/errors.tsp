import "@typespec/http";
import "@typespec/openapi";

using Http;
using OpenAPI;

namespace Orvano;

@doc("""
  An RFC 9457 problem details body. Every operation declares it as its `default` response, sent
  as `application/problem+json`.
  """)
@error
model Problem {
  @header contentType: "application/problem+json";

  @doc("Identifies the error: `https://orvano.dev/errors/<code>`.")
  @example("https://orvano.dev/errors/not_found")
  type: string;

  @doc("The standard HTTP reason phrase for the status.")
  @example("Not Found")
  title: string;

  @doc("The HTTP status code.")
  @example(404)
  status: int32;

  @doc("A short, safe sentence about this occurrence, when the server has one.")
  detail?: string;

  @doc("Orvano's stable error code, from the `ErrorCode` catalog. SDKs treat it as a plain string.")
  @example("not_found")
  code: string;

  @doc("The request ID to quote when reporting a problem; also sent as `X-Request-Id`.")
  @example("4bf92f3577b34da6a3ce929d0e0e4736")
  requestId: string;
}

@doc("Every stable error code the server sends in `Problem.code`.")
enum ErrorCode {
  @doc("Any failure the server did not expect (500).")
  internal_error,

  @doc("No such route or resource (404).")
  not_found,

  @doc("A bad parameter or body (400).")
  invalid_request,

  @doc("A pagination cursor the server can't read (400).")
  invalid_cursor,

  @doc("A `/v1/console` call without a valid console session, or with an API key or app session (401).")
  console_session_required,

  @doc("`Test` environment only: a response broke the API contract (500).")
  contract_violation,

  @doc("The project is unknown, purged, being deleted, or not yours to see (404).")
  project_not_found,

  @doc("The project is still provisioning, or provisioning failed (409).")
  project_not_ready,

  @doc("Your console role does not allow this action (403).")
  forbidden,

  @doc("The action would leave an org without an owner (409).")
  last_owner,

  @doc("The org still has projects that are not being deleted (409).")
  org_not_empty,

  @doc("The org is being deleted, so it and its projects can't change until it is restored (409).")
  org_not_active,

  @doc("Console sign up is invite only and no valid invitation was given (403).")
  signup_closed,

  @doc("The first console sign up on an install needs its setup token, and none or the wrong one was given (403).")
  setup_token_invalid,

  @doc("The new password is shorter than the project's minimum (8 to 64 characters, at least 8), or longer than 256, after Unicode NFKC normalization (400).")
  invalid_password,

  @doc("The new password is on the list of common passwords the project refuses (400).")
  password_too_common,

  @doc("The new password appears in a known data breach, by the Have I Been Pwned check the project turned on (400).")
  password_breached,

  @doc("The project closed sign ups, so this request can't create a user; existing users still sign in (403).")
  sign_up_disabled,

  @doc("The email's domain is not allowed by the project's rules: not on its allowed list, on its blocked list, or a disposable address (403).")
  email_domain_not_allowed,

  @doc("The project requires a verified email before sign in, and this user's email is not verified yet (403).")
  email_verification_required,

  @doc("The project has not turned on anonymous users, so guests can't sign in (403).")
  anonymous_disabled,

  @doc("An anonymous user (a guest) can't do this; upgrade them to a permanent account first (403).")
  anonymous_not_allowed,

  @doc("The email and password do not match a user, or the password given to confirm an action is missing or wrong (401).")
  invalid_credentials,

  @doc("The operation needs a signed in user's access token in `Authorization: Bearer` (401).")
  session_required,

  @doc("The access token is malformed, signed by an unknown key, for another project, or its session has ended (401).")
  invalid_token,

  @doc("The access token has expired; refresh the session and try again (401).")
  token_expired,

  @doc("The refresh token is malformed, reused, or its session has ended or expired; sign in again (401).")
  invalid_refresh_token,

  @doc("The API key is unknown, expired, or belongs to another project (401).")
  invalid_api_key,

  @doc("The user is blocked (403).")
  user_blocked,

  @doc("The API key lacks the scope this operation needs (403).")
  insufficient_scope,

  @doc("The browser origin matches none of the project's web platforms (403).")
  origin_not_allowed,

  @doc("A console call that changes something came from another site (403).")
  csrf_rejected,

  @doc("No such user in the project (404).")
  user_not_found,

  @doc("No such session for this user (404).")
  session_not_found,

  @doc("A user with this email already exists in the project (409).")
  user_already_exists,

  @doc("The invite link is unknown, already used, replaced, or revoked (404).")
  invitation_not_found,

  @doc("The invite link has expired; ask an org owner for a new one (410).")
  invitation_expired,

  @doc("The invitation is for another email than the console account using it (403).")
  invitation_email_mismatch,

  @doc("That email's console account is already a member of the org (409).")
  already_member,

  @doc("The org already holds 100 invitations; revoke some first (409).")
  invitation_limit,

  @doc("A project's SMTP host is, or resolves to, an address outside the public internet (400).")
  smtp_host_not_allowed,

  @doc("The SMTP server could not be reached: the name did not resolve, or the connection was refused or reset (502).")
  smtp_unreachable,

  @doc("The secure connection to the SMTP server failed: a bad certificate, or STARTTLS is not offered (502).")
  smtp_tls_failed,

  @doc("The SMTP server refused the username or password (502).")
  smtp_auth_failed,

  @doc("The SMTP server refused the email; `detail` carries its reply (502).")
  smtp_rejected,

  @doc("The SMTP server did not answer in time (504).")
  smtp_timeout,

  @doc("An email template failed to parse, names an unknown variable or filter, or is too costly to render (422).")
  template_invalid,

  @doc("Neither the project nor the install has an SMTP server set up (409).")
  email_not_configured,

  @doc("The project sent too many emails through the install's SMTP server this hour; wait for `Retry-After` (429).")
  email_rate_limited,

  @doc("Too many requests; wait for the number of seconds in `Retry-After` (429).")
  rate_limited,

  @doc("The redirect URL is malformed, or its host or scheme is not a platform of the project (400).")
  redirect_url_not_allowed,

  @doc("The email link's token is unknown, already used, expired, or replaced by a newer email (401).")
  invalid_email_token,

  @doc("The email code is wrong, expired, or used up; ask for a new one after 10 wrong tries (401).")
  invalid_code,

  @doc("Sign in again within 10 minutes before this action: a user without a password, or an old session removing a passkey (403).")
  reauthentication_required,

  @doc("The email is already verified (409).")
  email_already_verified,

  @doc("Another user of the project has this email (409).")
  email_already_in_use,

  @doc("The server is too busy to check a password right now; retry after `Retry-After` (503).")
  server_busy,

  @doc("The OAuth handoff code is unknown, already used, expired, or was redeemed without the verifier of the flow that started it (401).")
  invalid_oauth_code,

  @doc("The provider's ID token failed a check: signature, issuer, audience, expiry, nonce, or it was used before (401).")
  invalid_id_token,

  @doc("The user cancelled at the provider's consent screen (403).")
  oauth_access_denied,

  @doc("No such identity for this user (404).")
  identity_not_found,

  @doc("The provider is not turned on for the project (409).")
  provider_not_enabled,

  @doc("The provider is on, but its settings for this kind of sign in are missing (409).")
  provider_not_configured,

  @doc("The user already has an identity of this provider, with another provider account (409).")
  provider_already_linked,

  @doc("This provider account is linked to another user (409).")
  identity_already_linked,

  @doc("Removing this identity would leave the user with no way to sign in (409).")
  last_sign_in_method,

  @doc("The provider answered with an error, or its answer failed a check (502).")
  provider_error,

  @doc("The provider did not answer in time, or answered with a server error; try again (503).")
  provider_unavailable,

  @doc("The MFA ticket is unknown, expired, used up by 5 wrong factors, or from another project; sign in again (401).")
  invalid_mfa_ticket,

  @doc("The authenticator code or recovery code is wrong, or the code was already used (401).")
  invalid_mfa_code,

  @doc("The passkey's answer failed a check, or the passkey is unknown, inactive, or another user's (401).")
  invalid_passkey,

  @doc("The passkey challenge is unknown, used, expired, or not this user's; ask for a new one (400).")
  invalid_passkey_challenge,

  @doc("Pass a second factor (`account.verifyMfa`) within 10 minutes before this action (403).")
  mfa_verification_required,

  @doc("The access token's `aal` is below 2: this route needs a session that passed MFA (403).")
  mfa_required,

  @doc("The user already has an authenticator app turned on (409).")
  mfa_already_enabled,

  @doc("The user has no authenticator app turned on (409).")
  mfa_not_enabled,

  @doc("Verify the user's email before turning on MFA or adding a passkey (409).")
  email_not_verified,

  @doc("No authenticator app is waiting for its first code, or it waited more than 15 minutes; start again (409).")
  totp_not_pending,

  @doc("This factor is turned off for the project, or the user can't use it now (409).")
  factor_not_enabled,

  @doc("This passkey is already registered in the project (409).")
  passkey_already_registered,

  @doc("The user already has 10 passkeys (409).")
  passkey_limit,

  @doc("Passkeys are registered under the current RP ID; confirm the change with `confirmRpIdChange` (409).")
  passkeys_exist,

  @doc("No such passkey for this user (404).")
  passkey_not_found,
}

@doc("Error codes only the test operations send. Generated only into the scenario runners.")
@extension("x-orvano-test", true)
enum TestErrorCode {
  @doc("`test.conflict` always answers 409 with this code.")
  test_conflict,
}
