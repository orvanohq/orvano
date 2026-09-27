import 'dart:async';

/// A signed in user's session, as a client keeps it: what sign up, sign in,
/// and refresh return (spec 0004).
final class AuthSession {
  /// Creates a session from its tokens.
  const AuthSession({
    required this.accessToken,
    required this.accessTokenExpiresAt,
    required this.refreshToken,
    required this.refreshTokenExpiresAt,
    required this.sessionId,
  });

  /// Reads the `SessionTokens` a sign in or refresh returns. Throws a
  /// [FormatException] when a field is missing, which would mean the server
  /// broke the contract.
  factory AuthSession.fromJson(Object? json) {
    if (json is! Map<String, dynamic>) {
      throw const FormatException('Orvano: the response has no session');
    }
    String text(String key) => switch (json[key]) {
      final String value when value.isNotEmpty => value,
      _ => throw FormatException(
        'Orvano: the session in the response has no $key',
      ),
    };
    return AuthSession(
      accessToken: text('accessToken'),
      accessTokenExpiresAt: DateTime.parse(text('accessTokenExpiresAt')),
      refreshToken: text('refreshToken'),
      refreshTokenExpiresAt: DateTime.parse(text('refreshTokenExpiresAt')),
      sessionId: text('sessionId'),
    );
  }

  /// The access token, an ES256 JWT sent as `Authorization: Bearer`.
  final String accessToken;

  /// When the access token expires.
  final DateTime accessTokenExpiresAt;

  /// Trades itself for a new pair once.
  final String refreshToken;

  /// When the session ends unless it refreshes first.
  final DateTime refreshTokenExpiresAt;

  /// The session ID, also the access token's `sid` claim.
  final String sessionId;

  /// The session as JSON, the shape [AuthSession.fromJson] reads; for stores
  /// that keep it as text.
  Map<String, Object> toJson() => {
    'accessToken': accessToken,
    'accessTokenExpiresAt': accessTokenExpiresAt.toUtc().toIso8601String(),
    'refreshToken': refreshToken,
    'refreshTokenExpiresAt': refreshTokenExpiresAt.toUtc().toIso8601String(),
    'sessionId': sessionId,
  };
}

/// Where a client keeps the signed in user's session between calls. The
/// default keeps it in memory; `orvano_flutter` will keep it in secure
/// storage.
abstract interface class SessionStore {
  /// The current session, or null when nobody is signed in.
  FutureOr<AuthSession?> read();

  /// Saves a new session, or clears it with null.
  FutureOr<void> write(AuthSession? session);
}

/// A [SessionStore] that lives as long as the client: the default.
final class MemorySessionStore implements SessionStore {
  /// Creates a store, optionally holding [session] already.
  MemorySessionStore([this._session]);

  AuthSession? _session;

  @override
  AuthSession? read() => _session;

  @override
  void write(AuthSession? session) => _session = session;
}

/// The header the access token travels in, as `Bearer <token>`; named only
/// here in the Dart runtime.
const authorizationHeader = 'Authorization';
