// Auth policies (spec 0014): a project's password, sign up, session, and rate limit rules, edited on the console's
// Security page. Project scoped: every call carries X-Orvano-Project. The `console` project has fixed rules and no page.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("How many failed password sign ins one email may have from one address in a window.")
model SignInFailedLimit {
  @doc("Failed sign ins allowed per window, 3 to 100.")
  @example(10)
  limit: int32;

  @doc("The window in minutes, 1 to 1440.")
  @example(15)
  windowMinutes: int32;
}

@doc("A project's auth rules, as stored or at their defaults.")
model AuthPolicyValues {
  @doc("Whether new users can sign up from an app. Servers and the console can always create users.")
  @example(true)
  signUpsEnabled: boolean;

  @doc("Whether users must verify their email before a password sign in. Sign up then hides which emails have accounts.")
  @example(false)
  requireVerifiedEmail: boolean;

  @doc("Whether sign ups from known disposable email domains are refused.")
  @example(false)
  blockDisposableEmails: boolean;

  @doc("Email domains whose new users are refused, at most 500. A domain also covers its subdomains.")
  @example(#["example.com"])
  blockedEmailDomains: string[];

  @doc("When not empty, the only email domains new users may have, at most 500. A domain also covers its subdomains.")
  @example(#[])
  allowedEmailDomains: string[];

  @doc("The shortest new password, in characters, 8 to 64.")
  @example(8)
  passwordMinLength: int32;

  @doc("Whether new passwords on a list of common passwords are refused.")
  @example(true)
  passwordCommonCheck: boolean;

  @doc("Whether new passwords are checked against Have I Been Pwned, which receives 5 characters of the password's SHA-1.")
  @example(false)
  passwordBreachedCheck: boolean;

  @doc("How long an access token lasts, in seconds, 300 to 3600.")
  @example(900)
  accessTokenSeconds: int32;

  @doc("How long a session lasts without a refresh, in seconds, 3600 to 7776000, at most `sessionAbsoluteSeconds`.")
  @example(2592000)
  sessionIdleSeconds: int32;

  @doc("How long a session lasts at most, in seconds, 86400 to 31536000.")
  @example(31536000)
  sessionAbsoluteSeconds: int32;

  @doc("The most live sessions one user may hold, 1 to 1000; a new one ends the least recently used. Null means no limit.")
  @example(null)
  maxSessionsPerUser: int32 | null;

  @doc("""
    The app servers (address ranges such as `203.0.113.0/24`, at most 20) whose `X-Orvano-Client-IP` header names the
    visitor's address for rate limits.
    """)
  @example(#["203.0.113.0/24"])
  trustedServerCidrs: string[];

  @doc("Failed password sign ins per email and address.")
  signInFailedPerEmailIp: SignInFailedLimit;

  @doc("Failed password sign ins per address in 15 minutes, 10 to 10000.")
  @example(100)
  signInFailedPerIp: int32;

  @doc("Sign ups per address in an hour, 1 to 10000.")
  @example(60)
  signUpPerIp: int32;

  @doc("Guest sign ins per address in an hour, 1 to 10000.")
  @example(30)
  anonymousPerIp: int32;

  @doc("Emails a public request may send per address in an hour, 10 to 100000.")
  @example(300)
  emailSendPerIp: int32;
}

@doc("A project's auth rules with what the Security page shows beside them.")
model AuthPolicies {
  ...AuthPolicyValues;

  @doc("When the rules last changed; null while the project uses the defaults.")
  updatedAt: utcDateTime | null;

  @doc("Whether the project or the install has an email server, which requiring verified emails needs.")
  @example(true)
  smtpAvailable: boolean;

  @doc("Every rule at its default value, for the reset buttons.")
  defaults: AuthPolicyValues;
}

@doc("A change to failed password sign ins per email and address. Fields left out keep their value.")
model UpdateSignInFailedLimit {
  @doc("Failed sign ins allowed per window, 3 to 100.")
  @example(10)
  limit?: int32;

  @doc("The window in minutes, 1 to 1440.")
  @example(15)
  windowMinutes?: int32;
}

@doc("Changes to a project's auth rules. Fields left out keep their value; the result must meet every bound.")
model UpdateAuthPoliciesRequest {
  @doc("Whether new users can sign up from an app.")
  @example(true)
  signUpsEnabled?: boolean;

  @doc("Whether users must verify their email before a password sign in; turning it on needs an email server.")
  @example(true)
  requireVerifiedEmail?: boolean;

  @doc("Whether sign ups from known disposable email domains are refused.")
  @example(true)
  blockDisposableEmails?: boolean;

  @doc("Email domains whose new users are refused; replaces the list.")
  @example(#["example.com"])
  blockedEmailDomains?: string[];

  @doc("The only email domains new users may have; replaces the list, and empty allows any.")
  @example(#[])
  allowedEmailDomains?: string[];

  @doc("The shortest new password, in characters, 8 to 64.")
  @example(12)
  passwordMinLength?: int32;

  @doc("Whether new passwords on a list of common passwords are refused.")
  @example(true)
  passwordCommonCheck?: boolean;

  @doc("Whether new passwords are checked against Have I Been Pwned.")
  @example(true)
  passwordBreachedCheck?: boolean;

  @doc("How long an access token lasts, in seconds, 300 to 3600.")
  @example(900)
  accessTokenSeconds?: int32;

  @doc("How long a session lasts without a refresh, in seconds.")
  @example(2592000)
  sessionIdleSeconds?: int32;

  @doc("How long a session lasts at most, in seconds.")
  @example(31536000)
  sessionAbsoluteSeconds?: int32;

  @doc("The most live sessions one user may hold, 1 to 1000; null clears the limit.")
  @example(5)
  maxSessionsPerUser?: int32 | null;

  @doc("The app servers whose `X-Orvano-Client-IP` header is trusted; replaces the list.")
  @example(#["203.0.113.0/24"])
  trustedServerCidrs?: string[];

  @doc("Failed password sign ins per email and address; each field left out keeps its value.")
  signInFailedPerEmailIp?: UpdateSignInFailedLimit;

  @doc("Failed password sign ins per address in 15 minutes.")
  @example(100)
  signInFailedPerIp?: int32;

  @doc("Sign ups per address in an hour.")
  @example(60)
  signUpPerIp?: int32;

  @doc("Guest sign ins per address in an hour.")
  @example(30)
  anonymousPerIp?: int32;

  @doc("Emails a public request may send per address in an hour.")
  @example(300)
  emailSendPerIp?: int32;
}

@route("/v1/console/project/auth/policies")
@tag("console")
@useAuth(consoleSession)
interface ConsoleAuthPoliciesApi {
  @doc("Gets the project's auth rules; any member.")
  @operationId("consoleAuthPolicies.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthPolicies")
  @get
  get(): AuthPolicies | Problem;

  @doc("Changes the project's auth rules; owners and developers only.")
  @operationId("consoleAuthPolicies.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthPolicies")
  @patch(#{ implicitOptionality: false })
  update(@body request: UpdateAuthPoliciesRequest): AuthPolicies | Problem;
}
