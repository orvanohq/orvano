/// The core Dart client for Orvano. It carries only `client` and `both`
/// operations and has no way to set an API key. Apps use `orvano_flutter`;
/// servers use `orvano_dart`.
///
/// ```dart
/// final orvano = Orvano(Client(endpoint: 'https://orvano.example.com'));
/// final health = await orvano.health.get();
/// ```
library;

export 'src/client.dart';
export 'src/generated/models.dart';
export 'src/generated/services.dart';
export 'src/generated/version.dart';
export 'src/orvano_exception.dart';
