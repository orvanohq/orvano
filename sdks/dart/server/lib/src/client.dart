import 'package:orvano_core/orvano_core.dart' as core;

/// The header an API key travels in. Temporary: the auth spec (scope row 8)
/// replaces it, and this is the only place the Dart runtime names it.
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

  @override
  String get sdkName => 'orvano_dart';

  /// Sets the API key sent with every call, or removes it with null. Throws
  /// [UnsupportedError] in a browser.
  set apiKey(String? key) {
    if (_inBrowser) {
      throw UnsupportedError(
        'Orvano API keys are for trusted server code only. Never set one in '
        'a browser; use a session instead.',
      );
    }
    _apiKey = key;
  }

  @override
  Future<void> authorize(Map<String, String> headers) async {
    await super.authorize(headers);
    if (_apiKey case final key?) headers[apiKeyHeader] = key;
  }
}
