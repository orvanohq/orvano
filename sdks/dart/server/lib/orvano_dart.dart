/// The Orvano SDK for trusted Dart server code. It carries `server` and
/// `both` operations.
///
/// ```dart
/// final orvano = Orvano(Client(endpoint: 'https://orvano.example.com'));
/// final health = await orvano.health.get();
/// ```
library;

export 'src/generated/core_exports.dart';
export 'src/generated/services.dart';
