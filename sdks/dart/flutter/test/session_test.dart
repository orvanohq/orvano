import 'dart:convert';

import 'package:flutter/widgets.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

// Spec 0004 AC-25: the session survives a restart in secure storage under
// orvano.session.<projectId>, and a resume with an expiring access token
// refreshes before the next call.

AuthSession session(String token, Duration left) => AuthSession(
  accessToken: token,
  accessTokenExpiresAt: DateTime.now().toUtc().add(left),
  refreshToken: 'orv_rt_$token',
  refreshTokenExpiresAt: DateTime.now().toUtc().add(const Duration(days: 30)),
  sessionId: 's1',
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  test(
    'keeps the session under orvano.session.<projectId> across restarts',
    () async {
      final signedIn = session('a1', const Duration(minutes: 15));

      await SecureSessionStore('shop').write(signedIn);
      final restarted = await SecureSessionStore('shop').read();
      final stored = await const FlutterSecureStorage().read(
        key: 'orvano.session.shop',
      );

      expect(restarted?.accessToken, 'a1');
      expect(jsonDecode(stored!), signedIn.toJson());
    },
  );

  test(
    'clears the stored session on sign out, and reads junk as nobody',
    () async {
      FlutterSecureStorage.setMockInitialValues({
        'orvano.session.shop': 'not json',
      });
      final store = SecureSessionStore('shop');

      expect(await store.read(), isNull);
      await store.write(session('a1', const Duration(minutes: 15)));
      await store.write(null);
      expect(
        await const FlutterSecureStorage().read(key: 'orvano.session.shop'),
        isNull,
      );
    },
  );

  testWidgets('a resume with under a minute left refreshes', (tester) async {
    final paths = <String>[];
    FlutterSecureStorage.setMockInitialValues({
      'orvano.session.shop': jsonEncode(
        session('a1', const Duration(seconds: 20)).toJson(),
      ),
    });
    final client = createClient(
      endpoint: 'https://orvano.example.com',
      project: 'shop',
      onWarning: (_) {},
      httpClient: MockClient((request) async {
        paths.add(request.url.path);
        return http.Response(
          jsonEncode(session('a2', const Duration(minutes: 15)).toJson()),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );
    final events = <AuthEvent>[];
    client.authStateChanges.listen((c) => events.add(c.event));

    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 50)),
    );

    expect(paths, ['/v1/account/sessions/refresh']);
    expect((await client.session.read())?.accessToken, 'a2');
    expect(events, [AuthEvent.tokenRefreshed]);
    client.close();
  });

  test('an offline resume keeps the session', () async {
    final store = SecureSessionStore('shop');
    await store.write(session('a1', const Duration(seconds: 20)));
    final client = Client(
      endpoint: 'https://orvano.example.com',
      project: 'shop',
      session: store,
      onWarning: (_) {},
      httpClient: MockClient(
        (_) async => throw http.ClientException('offline'),
      ),
    );

    await checkSession(client);

    expect((await store.read())?.accessToken, 'a1');
    client.close();
  });
}
