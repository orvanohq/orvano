# orvano_flutter_passkeys

Native passkeys for [`orvano_flutter`](https://pub.dev/packages/orvano_flutter) apps on iOS, macOS, Android, and the web. It's an opt in add on: `orvano_flutter` has no passkey dependency of its own, so apps that don't use passkeys never carry one.

Under the hood it uses the [`passkeys`](https://pub.dev/packages/passkeys) package: `ASAuthorization` on iOS and macOS, Credential Manager on Android, and Corbado's web plugin in the browser.

## Use it

Add both packages, then pass `PlatformPasskeys()` to `createClient`:

```yaml
dependencies:
  orvano_flutter: ^0.2.0
  orvano_flutter_passkeys: ^0.2.0
```

```dart
import 'package:orvano_flutter/orvano_flutter.dart';
import 'package:orvano_flutter_passkeys/orvano_flutter_passkeys.dart';

final orvano = Orvano(
  createClient(
    endpoint: 'https://orvano.example.com',
    project: 'my-project',
    passkeys: PlatformPasskeys(),
  ),
);

await orvano.client.signInWithPasskey();
// A user with a password confirms it to add a passkey.
await orvano.client.registerPasskey(name: 'My phone', password: password);
```

`signInWithPasskey`, `registerPasskey`, and a passkey answer to `completeMfa` or `verifyMfa` (`MfaAnswer.passkey()`) all use it. Without it, those helpers throw an `ArgumentError` that names this package.

## Set up each platform

Passkeys belong to your project's RP ID (a domain you own, set on the project's Passkeys card in the Orvano console). Each platform has to prove your app may use it. The Passkeys card gives you both files below, filled in.

**iOS and macOS.** Add the Associated Domains capability with `webcredentials:<rpId>`, and serve `https://<rpId>/.well-known/apple-app-site-association` listing your app's `<TeamID>.<BundleID>` under `webcredentials`.

**Android.** Serve `https://<rpId>/.well-known/assetlinks.json` with the `delegate_permission/common.get_login_creds` relation for your package name and signing certificate's SHA-256 fingerprint, and add that fingerprint to the Android certificate fingerprints on the project's Passkeys card.

**Web.** Corbado's web plugin needs its script on the page. Download the `bundle.js` from the [flutter-passkeys releases](https://github.com/corbado/flutter-passkeys/releases) that matches the `passkeys_web` version in your `pubspec.lock`, save it in your app's `web/` folder, and load it in `web/index.html` before Flutter starts:

```html
<script src="bundle.js" type="application/javascript"></script>
<script src="flutter_bootstrap.js" async></script>
```

Serve the file from your own site rather than linking to GitHub, and update it when `passkeys_web` moves. A web build that has this package but not the script fails at startup. The web page must be served from your RP ID or one of its subdomains, as a web platform of the project.

Guide: https://orvano.dev/docs/auth/passkeys/

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview.

The package version shares the server's major.minor and moves in step with `orvano_flutter`.

This package lives in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
