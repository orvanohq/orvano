import 'dart:convert';

import 'package:clock/clock.dart';
import 'package:dart_jsonwebtoken/dart_jsonwebtoken.dart';
import 'package:orvano_core/orvano_core.dart' as core;

/// A user's access token that checked out: who, which session, and until
/// when.
final class VerifiedAccessToken {
  /// Creates the result of a check.
  const VerifiedAccessToken({
    required this.userId,
    required this.sessionId,
    required this.emailVerified,
    required this.expiresAt,
  });

  /// The user ID (the `sub` claim).
  final String userId;

  /// The session ID (the `sid` claim).
  final String sessionId;

  /// Whether the user's email was verified when the token was issued (the
  /// `email_verified` claim); false when the claim is missing. Up to 15
  /// minutes old: verify `online` for the current value.
  final bool emailVerified;

  /// When the token expires (the `exp` claim), in UTC.
  final DateTime expiresAt;
}

/// How long the project's signing keys are kept before they are fetched
/// again.
const keysLifetime = Duration(minutes: 10);

/// After a token names an unknown key, the keys are fetched again at most
/// this often.
const keysRefetchInterval = Duration(seconds: 30);

/// The clock skew allowed on `exp`.
const clockLeeway = Duration(seconds: 30);

/// Checks access tokens against one project's JWKS, keeping the keys for
/// [keysLifetime] (spec 0004, AC-19).
final class AccessTokenVerifier {
  /// A verifier that fetches through [client], which must name a project.
  AccessTokenVerifier(this._client);

  final core.Client _client;
  Map<String, JWTKey>? _keys;
  DateTime? _loadedAt;
  DateTime? _refetchedAt;
  Future<void>? _loading;

  /// See `Client.verifyAccessToken`.
  Future<VerifiedAccessToken> verify(
    String token, {
    bool online = false,
  }) async {
    final project = _client.project;
    if (project == null) {
      throw StateError(
        'Give the client a project to verify its access tokens.',
      );
    }

    final parts = token.split('.');
    final header = parts.length == 3 ? _decode(parts[0]) : null;
    if (header == null ||
        header['alg'] != 'ES256' ||
        header['kid'] is! String) {
      throw _invalid('The access token is not an ES256 JWT of Orvano.');
    }

    final key = await _keyFor(project, header['kid'] as String);
    if (key == null) {
      throw _invalid('The access token is signed by an unknown key.');
    }

    final Object? payload;
    try {
      payload = JWT
          .verify(
            token,
            key,
            checkHeaderType: false,
            checkExpiresIn: false,
            checkNotBefore: false,
            issuer: '${_client.endpoint}/v1/projects/$project',
            audience: Audience.one(project),
          )
          .payload;
    } on JWTException {
      throw _invalid('The access token is not valid for this project.');
    }

    if (payload case {
      'sub': final String userId,
      'sid': final String sessionId,
      'exp': final num exp,
    } when userId.isNotEmpty && sessionId.isNotEmpty) {
      final expiresAt = DateTime.fromMillisecondsSinceEpoch(
        (exp * 1000).toInt(),
        isUtc: true,
      );
      if (clock.now().toUtc().isAfter(expiresAt.add(clockLeeway))) {
        throw core.OrvanoException(
          status: 401,
          code: 'token_expired',
          message: 'The access token has expired; refresh the session.',
        );
      }

      if (online) await _client.send('GET', '/v1/account', bearer: token);
      return VerifiedAccessToken(
        userId: userId,
        sessionId: sessionId,
        emailVerified: payload['email_verified'] == true,
        expiresAt: expiresAt,
      );
    }

    throw _invalid('The access token names no user, session, or expiry.');
  }

  /// The key named [kid]. The keys are fetched when older than
  /// [keysLifetime], and again with `Cache-Control: no-cache` (at most once
  /// per [keysRefetchInterval]) when [kid] is not among them, so no cache in
  /// between serves the keys from before a rotation.
  Future<JWTKey?> _keyFor(String project, String kid) async {
    final now = clock.now();
    final loadedAt = _loadedAt;
    if (_keys == null ||
        loadedAt == null ||
        now.difference(loadedAt) >= keysLifetime) {
      await _load(project, noCache: false);
    } else if (!_keys!.containsKey(kid)) {
      final refetchedAt = _refetchedAt;
      if (refetchedAt == null ||
          now.difference(refetchedAt) >= keysRefetchInterval) {
        _refetchedAt = now;
        await _load(project, noCache: true);
      }
    }
    return _keys?[kid];
  }

  /// Fetches the JWKS, sharing one fetch between concurrent callers.
  Future<void> _load(
    String project, {
    required bool noCache,
  }) => _loading ??= () async {
    try {
      final body = await _client.send(
        'GET',
        '/v1/projects/${Uri.encodeComponent(project)}/.well-known/jwks.json',
        noCache: noCache,
      );
      final keys = <String, JWTKey>{};
      if (body case {'keys': final List<Object?> list}) {
        for (final jwk in list) {
          if (jwk case {'kid': final String kid, 'kty': 'EC', 'crv': 'P-256'}) {
            keys[kid] = JWTKey.fromJWK(Map<String, dynamic>.from(jwk));
          }
        }
      }
      _keys = keys;
      _loadedAt = clock.now();
    } finally {
      _loading = null;
    }
  }();

  static Map<String, Object?>? _decode(String part) {
    try {
      final json = jsonDecode(
        utf8.decode(base64Url.decode(base64Url.normalize(part))),
      );
      return json is Map<String, Object?> ? json : null;
    } on FormatException {
      return null;
    }
  }

  static core.OrvanoException _invalid(String message) => core.OrvanoException(
    status: 401,
    code: 'invalid_token',
    message: message,
  );
}
