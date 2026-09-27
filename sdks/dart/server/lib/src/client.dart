import 'package:orvano_core/orvano_core.dart' as core;

import 'access_tokens.dart';

/// The header an API key travels in (spec 0004, the `apiKey` scheme). This is
/// the only place the Dart runtime names it.
const apiKeyHeader = 'X-Orvano-Key';

const _inBrowser = bool.fromEnvironment('dart.library.js_interop');

/// Sends requests to one Orvano server from trusted server code. Like the
/// core client, plus an API key; it can also act as a user by carrying a
/// session.
final class Client extends core.Client {
  /// Creates a client for the server at [endpoint]. [apiKey] is sent with
  /// every call; it grants admin power, so setting one in a browser throws.
  Client({
    required super.endpoint,
    super.project,
    super.headers,
    super.session,
    super.timeout,
    super.maxRetries,
    super.httpClient,
    super.onWarning,
    String? apiKey,
  }) {
    if (apiKey != null) this.apiKey = apiKey;
  }

  String? _apiKey;
  late final _verifier = AccessTokenVerifier(this);

  @override
  String get sdkName => 'orvano_dart';

  /// Sets the API key sent with every call, or removes it with null. Throws
  /// [UnsupportedError] in a browser.
  set apiKey(String? key) {
    if (_inBrowser) {
      throw UnsupportedError(
        'Orvano API keys are for trusted server code only. Never set one in '
        'a browser; sign users in with a client SDK instead.',
      );
    }
    _apiKey = key;
  }

  /// Checks a user's access token without calling Orvano: an ES256 signature
  /// by one of the project's keys (fetched from its JWKS and kept for 10
  /// minutes), issued by this [endpoint] for this [project], and not expired
  /// (30 seconds leeway). A token whose session ended still passes until it
  /// expires (at most 15 minutes); pass [online] to also ask Orvano, as the
  /// user and never with the API key, whether the session is still active.
  ///
  /// Throws [core.OrvanoException] with status 401 and code `token_expired`
  /// or `invalid_token` when the token does not check out, and [StateError]
  /// when the client has no [project].
  Future<VerifiedAccessToken> verifyAccessToken(
    String token, {
    bool online = false,
  }) => _verifier.verify(token, online: online);

  @override
  Future<void> authorize(Map<String, String> headers) async {
    await super.authorize(headers);
    if (_apiKey case final key?) headers[apiKeyHeader] = key;
  }
}
