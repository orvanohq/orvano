import 'dart:io';

import 'package:orvano_dart/orvano_dart.dart';

/// The server client. It sends your API key with every call and checks your
/// users' access tokens against the project's public keys.
final client = Client(
  endpoint: _setting('ORVANO_ENDPOINT', fallback: 'http://localhost:7700'),
  project: _setting('ORVANO_PROJECT'),
  apiKey: _setting('ORVANO_API_KEY'),
);

/// Every server operation, grouped by service: `orvano.users.get(...)`.
final orvano = Orvano(client);

/// Reads a setting from the environment, so the API key never sits in code.
String _setting(String name, {String? fallback}) {
  final value = Platform.environment[name] ?? fallback ?? '';
  if (value.isEmpty) throw StateError('Set the $name environment variable.');
  return value;
}
