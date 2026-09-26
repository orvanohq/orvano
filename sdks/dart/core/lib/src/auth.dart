import 'dart:async';

/// Where a client keeps the signed in user's session token between calls.
/// The default keeps it in memory; `orvano_flutter` will keep it in secure
/// storage.
abstract interface class SessionStore {
  /// The current token, or null when nobody is signed in.
  FutureOr<String?> read();

  /// Saves a new token, or clears it with null.
  FutureOr<void> write(String? token);
}

/// A [SessionStore] that lives as long as the client: the default.
final class MemorySessionStore implements SessionStore {
  /// Creates a store, optionally holding [token] already.
  MemorySessionStore([this._token]);

  String? _token;

  @override
  String? read() => _token;

  @override
  void write(String? token) => _token = token;
}

/// The header an app session travels in. Temporary: the auth spec (scope row
/// 8) replaces it, and this is the only place the Dart runtime names it.
const sessionHeader = 'X-Orvano-Session';
