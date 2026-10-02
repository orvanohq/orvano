// The CI check for the Flutter quickstart (spec 0011, AC-11, AC-19, AC-20): sign
// up, see your name and email, sign out, sign in again, and sign out, against a
// running local stack. Run it with the same --dart-define settings as the app.
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_quickstart/main.dart' as app;
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('sign up, sign in, and sign out', (tester) async {
    final id = Random().nextInt(1 << 30).toRadixString(36);
    final email = 'flutter-quickstart-$id@example.com';
    const password = 'a quickstart password';
    const name = 'Grace Hopper';

    await app.main();
    await settle(tester, find.text('Sign up'));

    await tester.enterText(find.byKey(const Key('Sign up name')), name);
    await tester.enterText(find.byKey(const Key('Sign up email')), email);
    await tester.enterText(find.byKey(const Key('Sign up password')), password);
    await tester.tap(find.byKey(const Key('Sign up button')));
    await expectSignedIn(tester, name, email);

    await tester.tap(find.text('Sign out'));
    await settle(tester, find.byKey(const Key('Sign in button')));
    await tester.enterText(find.byKey(const Key('Sign in email')), email);
    await tester.enterText(find.byKey(const Key('Sign in password')), password);
    await tester.tap(find.byKey(const Key('Sign in button')));
    await expectSignedIn(tester, name, email);

    await tester.tap(find.text('Sign out'));
    await settle(tester, find.byKey(const Key('Sign in button')));
  });
}

Future<void> expectSignedIn(
  WidgetTester tester,
  String name,
  String email,
) async {
  await settle(tester, find.text("You're signed in"));
  expect(find.text('Name: $name'), findsOneWidget);
  expect(find.text('Email: $email'), findsOneWidget);
}

/// Pumps until [finder] shows up, for at most 20 seconds: the calls go to a real
/// server, so a fixed number of frames isn't enough.
Future<void> settle(WidgetTester tester, Finder finder) async {
  final deadline = DateTime.now().add(const Duration(seconds: 20));
  while (finder.evaluate().isEmpty) {
    if (DateTime.now().isAfter(deadline)) {
      throw TestFailure('Timed out waiting for $finder.');
    }
    await tester.pump(const Duration(milliseconds: 100));
  }
}
