/// Orvano for Flutter apps. It re-exports `orvano_core`, which carries only
/// `client` and `both` operations, so no API key can end up in an app.
/// Secure session storage, deep links, and push glue arrive here as their
/// features are built.
///
/// ```dart
/// final orvano = Orvano(Client(endpoint: 'https://orvano.example.com'));
/// final health = await orvano.health.get();
/// ```
library;

export 'package:orvano_core/orvano_core.dart';
