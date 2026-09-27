import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:orvano_core/orvano_core.dart';

/// A [SessionStore] in the platform's secure storage (the Keychain on iOS
/// and macOS, the Keystore on Android, encrypted storage on the web) under
/// `orvano.session.<projectId>` (spec 0004, AC-25), so the user stays
/// signed in across app restarts. The session is also kept in memory after
/// the first read, so calls don't wait on the platform store.
final class SecureSessionStore implements SessionStore {
  /// A store for [project]'s session. Pass [storage] to use your own
  /// platform options.
  SecureSessionStore(String project, {FlutterSecureStorage? storage})
    : key = sessionStorageKey(project),
      _storage = storage ?? const FlutterSecureStorage();

  /// The key the session is stored under.
  final String key;

  final FlutterSecureStorage _storage;
  AuthSession? _cached;
  bool _loaded = false;

  @override
  Future<AuthSession?> read() async {
    if (_loaded) return _cached;
    final text = await _storage.read(key: key);
    _cached = _parse(text);
    _loaded = true;
    return _cached;
  }

  @override
  Future<void> write(AuthSession? session) async {
    _cached = session;
    _loaded = true;
    if (session == null) {
      await _storage.delete(key: key);
    } else {
      await _storage.write(key: key, value: jsonEncode(session.toJson()));
    }
  }

  static AuthSession? _parse(String? text) {
    if (text == null) return null;
    try {
      return AuthSession.fromJson(jsonDecode(text));
    } on FormatException {
      // Not a session this SDK wrote: nobody is signed in.
      return null;
    }
  }
}
