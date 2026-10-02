# Orvano Flutter quickstart

The finished app of the [Flutter quickstart](https://orvano.dev/docs/quickstarts/flutter/): sign up, sign in, see your name and email, and sign out, with `orvano_flutter` on the web, iOS, and Android. The session lives in the platform's secure storage.

To run it, start Orvano locally ([Run Orvano locally](https://orvano.dev/docs/local/)) and add the platforms from the quickstart to your project (a Web platform `localhost`, and the iOS bundle ID and Android package name `dev.orvano.quickstart`), then:

```bash
flutter pub get
flutter run -d chrome --web-port 5050 --dart-define=ORVANO_PROJECT=your-project-id
```

On the Android emulator, first run `adb reverse tcp:7700 tcp:7700` so the app reaches Orvano at `http://localhost:7700`.

`flutter test integration_test --dart-define=ORVANO_PROJECT=your-project-id` runs the same steps the quickstart shows, on a device or emulator.
