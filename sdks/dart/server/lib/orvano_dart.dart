/// The Orvano SDK for trusted Dart server code. It carries `server` and
/// `both` operations and accepts an API key.
///
/// ```dart
/// final orvano = Orvano(
///   Client(endpoint: 'https://orvano.example.com', apiKey: apiKey),
/// );
/// final health = await orvano.health.get();
/// ```
library;

export 'src/client.dart' show Client;
export 'src/generated/core_exports.dart';
export 'src/generated/services.dart';
