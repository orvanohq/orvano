# orvano_core

The core Dart client for Orvano. Flutter apps use `orvano_flutter`, and Dart servers use `orvano_dart`; both build on this package.

## Use it

```dart
import 'package:orvano_core/orvano_core.dart';

final orvano = Orvano(Client(endpoint: 'https://orvano.example.com'));
final health = await orvano.health.get();
```

`orvano.client.handleLink(uri)` redeems any link Orvano emails (verification, password reset, magic link, email change); see `orvano_flutter` for wiring deep links. Every failure is an `OrvanoException`; a limit's refusal carries `retryAfter`.

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview and only covers the health check so far.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
