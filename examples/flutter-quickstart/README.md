# Orvano Flutter quickstart

The finished app of the [Flutter quickstart](https://orvano.dev/docs/quickstarts/flutter/): sign up, sign in, see your name and email, and sign out, with `orvano_flutter` on the web, iOS, and Android. The session lives in the platform's secure storage.

To run it, start Orvano locally ([Run Orvano locally](https://orvano.dev/docs/local/)) and add the platforms from the quickstart to your project (a Web platform `localhost`, and the iOS bundle ID and Android package name `dev.orvano.quickstart`), then:

```bash
flutter pub get
flutter run -d chrome --web-port 5050 --dart-define=ORVANO_PROJECT=your-project-id
```

On the Android emulator, first run `adb reverse tcp:7700 tcp:7700` so the app reaches Orvano at `http://localhost:7700`.

`flutter test integration_test --dart-define=ORVANO_PROJECT=your-project-id` runs the same steps the quickstart shows, on a device or emulator.

## Sign in with a provider

`lib/main_providers.dart` is the same app with Google, Apple, GitHub, and Microsoft sign in. Google signs in natively on iOS and Android, Apple natively on iOS, and the rest by redirect through the system's sign in sheet, which comes back on the app's scheme `dev.orvano.quickstart://auth`. Signed in, it lists your linked providers with **Unlink**, and a **Link** button for the others. It runs on iOS, Android, and macOS, not the web. On macOS every provider signs in by redirect.

1. Turn each provider on in your project's **Sign in methods** page: [Sign in with Google](https://orvano.dev/docs/auth/sign-in-with-google/) and the Apple, GitHub, and Microsoft pages show the setup, and [Native sign in](https://orvano.dev/docs/auth/native-sign-in/) covers the native client IDs.
2. For Google on iOS, copy `ios/Flutter/GoogleSignIn.xcconfig.example` to `ios/Flutter/GoogleSignIn.xcconfig` and set your reversed iOS client ID. For Google on Android, register your signing key's SHA-1 with an Android OAuth client at Google.
3. For Apple, give the App ID `dev.orvano.quickstart` the Sign in with Apple capability, and pick your team in Xcode.
   On macOS, open `macos/Runner.xcworkspace` and pick your team there too: the app keeps its session in the Keychain, which needs development signing.
4. Run it:

```bash
flutter run -t lib/main_providers.dart --dart-define=ORVANO_PROJECT=your-project-id --dart-define=GOOGLE_WEB_CLIENT_ID=your-web-client-id --dart-define=GOOGLE_IOS_CLIENT_ID=your-ios-client-id
```

On macOS, add `-d macos` to that command; the Google client IDs aren't needed there.
