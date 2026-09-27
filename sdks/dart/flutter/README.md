# orvano_flutter

Orvano for Flutter apps on iOS, Android, and the web, built on `orvano_core`.

## Use it

```dart
import 'package:flutter/widgets.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

WidgetsFlutterBinding.ensureInitialized();
final orvano = Orvano(
  createClient(endpoint: 'https://orvano.example.com', project: 'my-project'),
);
orvano.client.authStateChanges.listen((change) => print(change.event));
```

`createClient` keeps the signed in user's session in secure storage (the Keychain, the Keystore, or encrypted web storage) under `orvano.session.<project>`, so it survives restarts, and checks it for a refresh each time the app resumes.

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
