/// Native passkeys for Orvano Flutter apps (spec 0013, AC-38): an opt in add
/// on to `orvano_flutter`, so apps that don't use passkeys never carry the
/// `passkeys` package. Pass [PlatformPasskeys] to `createClient`.
///
/// ```dart
/// import 'package:orvano_flutter/orvano_flutter.dart';
/// import 'package:orvano_flutter_passkeys/orvano_flutter_passkeys.dart';
///
/// final orvano = Orvano(
///   createClient(
///     endpoint: 'https://orvano.example.com',
///     project: 'shop',
///     passkeys: PlatformPasskeys(),
///   ),
/// );
/// await orvano.client.signInWithPasskey();
/// ```
library;

export 'src/platform_passkeys.dart' show PlatformPasskeys;
