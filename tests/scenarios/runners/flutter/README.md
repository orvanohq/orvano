# Flutter scenario runner

Runs the shared scenarios (`tests/scenarios/*.yaml`) through `orvano_flutter` on a device. Start
the scenario server first (`docker compose -f tests/scenarios/compose.yml up -d --build --wait`
from the repo root), then, from this directory:

```bash
dart run tool/copy_scenarios.dart
```

```bash
flutter test integration_test -d <ios simulator id> --dart-define=ORVANO_ENDPOINT=http://localhost:8080
```

On an Android emulator, run `adb reverse tcp:8080 tcp:8080` first and keep
`http://localhost:8080`: the endpoint must match the server's `ORVANO_PUBLIC_URL`, or access
tokens don't verify against their issuer. On Chrome, run
it through `flutter drive` with `chromedriver --port=4444` running:

```bash
flutter drive -d web-server --browser-name=chrome --driver=test_driver/integration_test.dart --target=integration_test/scenarios_test.dart --web-browser-flag=--disable-web-security --dart-define=ORVANO_ENDPOINT=http://localhost:8080
```

The web run disables Chrome's same origin checks because the app and the server are on different
ports here; in a real deployment the app is served from the Orvano domain or CORS allows it.
