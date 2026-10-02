/// Where Orvano runs: `--dart-define=ORVANO_ENDPOINT=...`, the local stack by
/// default. On the Android emulator, `adb reverse` makes `localhost` reach it.
const endpoint = String.fromEnvironment(
  'ORVANO_ENDPOINT',
  defaultValue: 'http://localhost:7700',
);

/// Your project's ID, from its overview in the Orvano console:
/// `--dart-define=ORVANO_PROJECT=...`.
const project = String.fromEnvironment('ORVANO_PROJECT');
