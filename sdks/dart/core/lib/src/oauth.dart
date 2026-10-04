import 'dart:convert';
import 'dart:math';

import 'package:crypto/crypto.dart';

import 'auth.dart';
import 'client.dart';
import 'generated/models.dart';
import 'generated/services.dart';
import 'links.dart';
import 'orvano_exception.dart';

/// Opens a provider's sign in page at [url] and completes with the URL the
/// browser was sent back to, which starts with [redirectUrl] (spec 0012,
/// AC-22). `orvano_flutter` gives every client a default one built on
/// `flutter_web_auth_2`; a Dart app passes its own.
typedef OAuthLauncher = Future<Uri> Function(Uri url, Uri redirectUrl);

/// What a provider redirect came back for: its `orvano_type` parameter.
enum OAuthLinkType {
  /// A sign in (`account.createOAuthSession`).
  oauth('oauth'),

  /// Linking a provider to the signed in user (`account.completeOAuthLink`).
  oauthLink('oauth_link');

  const OAuthLinkType(this.wire);

  /// The `orvano_type` value.
  final String wire;
}

/// The query parameter Orvano sets to the handoff code after a provider
/// redirect.
const codeParameter = 'orvano_code';

/// The query parameter Orvano sets to the error code when a provider redirect
/// failed.
const errorParameter = 'orvano_error';

/// A nonce for native sign in: keep [raw] for Orvano, give the provider
/// [hashed].
final class OrvanoNonce {
  const OrvanoNonce._(this.raw, this.hashed);

  /// A new nonce: 32 random bytes as base64url, and the lowercase hex SHA-256
  /// of that string, which Google or Apple put in the token (spec 0012,
  /// AC-22).
  factory OrvanoNonce.create() {
    final raw = _base64Url(_randomBytes(32));
    return OrvanoNonce._(raw, sha256.convert(utf8.encode(raw)).toString());
  }

  /// The raw value: pass it to `signInWithIdToken`.
  final String raw;

  /// Its lowercase hex SHA-256: pass it to Google or Apple sign in.
  final String hashed;
}

final _verifiers = Expando<String>('orvano.oauth.verifier');
final _launchers = Expando<OAuthLauncher>('orvano.oauth.launcher');

/// Gives [client] the launcher its OAuth helpers use when none is passed.
/// `orvano_flutter`'s `createClient` sets one built on `flutter_web_auth_2`.
void setDefaultOAuthLauncher(Client client, OAuthLauncher launcher) {
  _launchers[client] = launcher;
}

/// Provider sign in, linking, and native ID tokens (spec 0012, AC-22).
extension OAuthSignIn on Client {
  /// Signs in with [provider] by redirect: makes a PKCE verifier and keeps it
  /// in memory, starts the flow, opens the provider through [launcher] (or
  /// the client's default), and redeems the URL it returns.
  ///
  /// Throws an [ArgumentError], before any call, when there is no launcher,
  /// and an [OrvanoException] for a refused start or redemption, or for an
  /// `orvano_error` the provider redirect carried (`oauth_access_denied`, ...).
  Future<OAuthSignInResult> signInWithOAuth(
    OAuthProvider provider, {
    required Uri redirectUrl,
    OAuthLauncher? launcher,
    RequestOptions? options,
  }) async =>
      await _runFlow(
            OAuthLinkType.oauth,
            provider,
            redirectUrl,
            launcher,
            options,
          )
          as OAuthSignInResult;

  /// Links [provider] to the signed in user by redirect, like
  /// [signInWithOAuth]. The session must be at most 10 minutes old
  /// (`reauthentication_required`).
  Future<IdentityLinkResult> linkIdentity(
    OAuthProvider provider, {
    required Uri redirectUrl,
    OAuthLauncher? launcher,
    RequestOptions? options,
  }) async =>
      await _runFlow(
            OAuthLinkType.oauthLink,
            provider,
            redirectUrl,
            launcher,
            options,
          )
          as IdentityLinkResult;

  /// Signs in with a provider's ID token from native Google or Apple sign
  /// in, with the raw nonce from [OrvanoNonce.create] (spec 0012, AC-9). For
  /// Apple, pass the [authorizationCode] Sign in with Apple returned, and the
  /// [name] it returned on the first authorization. Stores the session.
  Future<OAuthSignInResult> signInWithIdToken({
    required IdTokenProvider provider,
    required String idToken,
    required String nonce,
    String? authorizationCode,
    String? name,
    RequestOptions? options,
  }) async {
    final result = await AccountService(this).createIdTokenSession(
      CreateIdTokenSessionRequest(
        provider: provider,
        idToken: idToken,
        nonce: nonce,
        authorizationCode: authorizationCode,
        name: name,
      ),
      options: options,
    );
    return OAuthSignInResult(user: result.user, isNewUser: result.isNewUser);
  }

  /// Links a provider to the signed in user with its native ID token, and
  /// emits [AuthEvent.userUpdated].
  Future<Identity> linkIdentityWithIdToken({
    required IdTokenProvider provider,
    required String idToken,
    required String nonce,
    String? authorizationCode,
    String? name,
    RequestOptions? options,
  }) async {
    final identity = await AccountService(this).createIdTokenIdentity(
      CreateIdTokenSessionRequest(
        provider: provider,
        idToken: idToken,
        nonce: nonce,
        authorizationCode: authorizationCode,
        name: name,
      ),
      options: options,
    );
    await reloadSession(AuthEvent.userUpdated);
    return identity;
  }

  /// Redeems a provider redirect (what `handleLink` does for `oauth` and
  /// `oauth_link`); null when [uri] carries no OAuth `orvano_type`.
  Future<HandledLink?> handleOAuthRedirect(
    Uri uri, {
    RequestOptions? options,
  }) async {
    final type = OAuthLinkType.values
        .where((t) => t.wire == uri.queryParameters['orvano_type'])
        .firstOrNull;
    if (type == null) return null;
    final error = uri.queryParameters[errorParameter];
    if (error != null) {
      _verifiers[this] = null;
      throw oauthRedirectError(error);
    }
    final code = uri.queryParameters[codeParameter];
    if (code == null || code.isEmpty) {
      throw ArgumentError.value(code, codeParameter, 'is missing');
    }
    final verifier = _verifiers[this];
    if (verifier == null) {
      throw ArgumentError(
        'No PKCE verifier is stored for this flow; start it again with '
        'signInWithOAuth.',
      );
    }
    _verifiers[this] = null;
    final account = AccountService(this);
    switch (type) {
      case OAuthLinkType.oauth:
        final result = await account.createOAuthSession(
          CreateOAuthSessionRequest(code: code, codeVerifier: verifier),
          options: options,
        );
        return OAuthSignInResult(
          user: result.user,
          isNewUser: result.isNewUser,
        );
      case OAuthLinkType.oauthLink:
        final identity = await account.completeOAuthLink(
          CompleteOAuthLinkRequest(code: code, codeVerifier: verifier),
          options: options,
        );
        await reloadSession(AuthEvent.userUpdated);
        return IdentityLinkResult(identity: identity);
    }
  }

  Future<HandledLink> _runFlow(
    OAuthLinkType type,
    OAuthProvider provider,
    Uri redirectUrl,
    OAuthLauncher? launcher,
    RequestOptions? options,
  ) async {
    final open = launcher ?? _launchers[this];
    if (open == null) {
      throw ArgumentError(
        'Pass a launcher: orvano_core has no default way to open a browser.',
      );
    }
    final verifier = _base64Url(_randomBytes(32));
    final challenge = _base64Url(sha256.convert(ascii.encode(verifier)).bytes);
    final account = AccountService(this);
    final body = CreateOAuthFlowRequest(
      provider: provider,
      redirectUrl: redirectUrl.toString(),
      codeChallenge: challenge,
    );
    final flow = type == OAuthLinkType.oauth
        ? await account.createOAuthFlow(body, options: options)
        : await account.createOAuthLinkFlow(body, options: options);
    _verifiers[this] = verifier;
    final back = await open(Uri.parse(flow.url), redirectUrl);
    final result = await handleOAuthRedirect(back, options: options);
    if (result == null) {
      throw ArgumentError.value(
        back,
        'launcher',
        'returned a URL that is not a provider redirect',
      );
    }
    return result;
  }
}

/// The error a provider redirect carried, as the typed error every SDK call
/// throws, with the status the code has in Orvano's error catalog.
OrvanoException oauthRedirectError(String code) => OrvanoException(
  status: switch (code) {
    'oauth_access_denied' => 403,
    'provider_error' => 502,
    'provider_unavailable' => 503,
    'provider_not_enabled' || 'provider_not_configured' => 409,
    _ => 400,
  },
  code: code,
  message: 'Sign in with the provider did not finish ($code).',
);

List<int> _randomBytes(int length) {
  final random = Random.secure();
  return List<int>.generate(length, (_) => random.nextInt(256));
}

String _base64Url(List<int> bytes) =>
    base64Url.encode(bytes).replaceAll('=', '');
