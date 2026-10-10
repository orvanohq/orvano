@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0014 AC-36: guest sign in stores its session through the generated
// call, and an upgrade refreshes the session so the next token says the user
// is permanent, unless it waits for its emailed link.

String jwt(Map<String, Object?> claims) {
  String part(Object value) =>
      base64Url.encode(utf8.encode(jsonEncode(value))).replaceAll('=', '');
  return '${part({'alg': 'ES256'})}.${part(claims)}.sig';
}

AuthSession session({
  required bool anonymous,
  String sid = 's1',
}) => AuthSession(
  accessToken: jwt({
    'sub': 'u1',
    'sid': sid,
    'email_verified': false,
    'is_anonymous': anonymous,
  }),
  accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 15)),
  refreshToken: 'orv_rt_x',
  refreshTokenExpiresAt: DateTime.now().toUtc().add(const Duration(days: 30)),
  sessionId: sid,
);

Map<String, Object?> user({required bool anonymous}) => {
  'id': 'u1',
  'email': anonymous ? null : 'ada@example.com',
  'emailVerified': false,
  'emailVerifiedAt': null,
  'name': null,
  'status': 'active',
  'metadata': <String, Object?>{},
  'createdAt': '2026-10-09T00:00:00Z',
  'lastSignInAt': null,
  'providers': <String>[],
  'hasPassword': !anonymous,
  'mfaEnabled': false,
  'isAnonymous': anonymous,
};

Answer json(Object body, {int code = 200}) => (response) async {
  response
    ..statusCode = code
    ..headers.contentType = ContentType.json
    ..write(jsonEncode(body));
  await response.close();
};

const upgrade = CreateAnonymousUpgradeRequest(
  email: 'ada@example.com',
  password: 'correct horse battery',
);

void main() {
  late FakeOrvano server;
  late Client client;
  late List<AuthEvent> seen;

  Future<void> serve(List<Answer> answers, {AuthSession? signedIn}) async {
    server = await FakeOrvano.start(answers);
    client = Client(
      endpoint: server.endpoint,
      project: 'shop',
      session: MemorySessionStore(signedIn),
      maxRetries: 0,
      onWarning: (_) {},
    );
    seen = [];
    client.authStateChanges.listen((c) => seen.add(c.event));
  }

  tearDown(() async {
    client.close();
    await server.close();
  });

  test('a guest sign in stores the session', () async {
    await serve([
      json({
        'user': user(anonymous: true),
        'session': session(anonymous: true).toJson(),
        'mfa': null,
        'isNewUser': true,
        'verificationEmail': null,
        'verificationRequired': false,
      }, code: 201),
    ]);

    final result = await Orvano(client).account.createAnonymousSession();
    await Future<void>.delayed(Duration.zero);

    expect(result.user?.isAnonymous, isTrue);
    expect(server.paths, ['/v1/account/sessions/anonymous']);
    expect((await client.session.read())?.sessionId, 's1');
    expect(seen, [AuthEvent.signedIn]);
  });

  test('an upgrade that made the user permanent refreshes', () async {
    await serve([
      json({
        'user': user(anonymous: false),
        'verificationRequired': false,
        'verificationEmail': null,
      }),
      json(session(anonymous: false).toJson()),
    ], signedIn: session(anonymous: true));

    final result = await Orvano(client).account.upgradeAnonymous(upgrade);
    await Future<void>.delayed(Duration.zero);

    expect(result.user?.isAnonymous, isFalse);
    expect(server.paths, [
      '/v1/account/anonymous/upgrade',
      '/v1/account/sessions/refresh',
    ]);
    expect(seen, [AuthEvent.tokenRefreshed, AuthEvent.userUpdated]);
  });

  test('an upgrade waiting for its emailed link changes nothing', () async {
    await serve([
      json({
        'user': null,
        'verificationRequired': true,
        'verificationEmail': null,
      }),
    ], signedIn: session(anonymous: true));

    final result = await Orvano(client).account.upgradeAnonymous(upgrade);
    await Future<void>.delayed(Duration.zero);

    expect(result.verificationRequired, isTrue);
    expect(server.paths, ['/v1/account/anonymous/upgrade']);
    expect(seen, isEmpty);
  });
}
