@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0013 AC-38: a sign in that stops at the MFA step stores nothing,
// keeps the ticket in memory, and emits mfaRequired; completeMfa finishes it;
// verifyMfa and confirmTotp store the new access token.

const ticket = 'orv_mt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';

Map<String, Object?> tokens(String access, {String sid = 's1'}) => {
  'accessToken': access,
  'accessTokenExpiresAt': DateTime.now()
      .toUtc()
      .add(const Duration(minutes: 15))
      .toIso8601String(),
  'refreshToken': 'orv_rt_$sid',
  'refreshTokenExpiresAt': DateTime.now()
      .toUtc()
      .add(const Duration(days: 30))
      .toIso8601String(),
  'sessionId': sid,
};

final user = {
  'id': 'u1',
  'email': 'ada@example.com',
  'emailVerified': true,
  'emailVerifiedAt': null,
  'name': null,
  'status': 'active',
  'metadata': <String, Object?>{},
  'createdAt': '2026-01-01T00:00:00Z',
  'lastSignInAt': null,
  'providers': <String>[],
  'hasPassword': true,
  'mfaEnabled': false,
};

Answer json(Object body, {int status = 200}) => (response) async {
  response
    ..statusCode = status
    ..headers.contentType = ContentType.json
    ..write(jsonEncode(body));
  await response.close();
};

Answer challenged() => json({
  'user': null,
  'session': null,
  'mfa': {
    'ticket': ticket,
    'factors': ['totp', 'recovery_code'],
    'expiresAt': '2026-10-07T12:05:00Z',
  },
  'isNewUser': false,
  'verificationEmail': null,
}, status: 201);

Answer signedIn() => json({
  'user': user,
  'session': tokens('level2', sid: 's2'),
  'mfa': null,
  'isNewUser': false,
  'verificationEmail': null,
}, status: 201);

void main() {
  late FakeOrvano server;
  late Client client;

  Future<void> serve(List<Answer> answers, {AuthSession? signedIn}) async {
    server = await FakeOrvano.start(answers);
    client = Client(
      endpoint: server.endpoint,
      project: 'shop',
      session: MemorySessionStore(signedIn),
      maxRetries: 0,
      onWarning: (_) {},
    );
  }

  tearDown(() async {
    client.close();
    await server.close();
  });

  Future<void> signIn() => Orvano(client).account.createPasswordSession(
    const CreatePasswordSessionRequest(
      email: 'ada@example.com',
      password: 'pw',
    ),
  );

  test(
    'step one stores nothing and emits mfaRequired with the factors',
    () async {
      await serve([challenged()]);
      final changes = <AuthStateChange>[];
      client.authStateChanges.listen(changes.add);

      await signIn();
      await Future<void>.delayed(Duration.zero);

      expect(await client.session.read(), isNull);
      expect(changes.single.event, AuthEvent.mfaRequired);
      expect(changes.single.mfa?.factors, [
        MfaFactor.totp,
        MfaFactor.recoveryCode,
      ]);
      expect(client.pendingMfa?.expiresAt, DateTime.utc(2026, 10, 7, 12, 5));
    },
  );

  test(
    'completeMfa sends the ticket and code, stores the session, and emits signedIn',
    () async {
      await serve([challenged(), signedIn()]);
      final events = <AuthEvent>[];
      client.authStateChanges.listen((c) => events.add(c.event));
      await signIn();

      final signedInUser = await client.completeMfa(
        const MfaAnswer.totp('123456'),
      );
      await Future<void>.delayed(Duration.zero);

      expect(signedInUser.id, 'u1');
      expect(server.paths[1], '/v1/account/sessions/mfa');
      expect(jsonDecode(server.bodies[1]), {
        'ticket': ticket,
        'totpCode': '123456',
      });
      expect(server.requests[1].value('Authorization'), isNull);
      expect((await client.session.read())?.sessionId, 's2');
      expect(events, [AuthEvent.mfaRequired, AuthEvent.signedIn]);
      expect(client.pendingMfa, isNull);
    },
  );

  test(
    'an ended ticket forgets the pending sign in; none throws before any call',
    () async {
      await serve([
        challenged(),
        problem(401, {'status': 401, 'code': 'invalid_mfa_ticket'}),
      ]);
      await signIn();

      await expectLater(
        client.completeMfa(const MfaAnswer.recoveryCode('AAAAA-BBBBB')),
        throwsA(isA<OrvanoException>()),
      );
      expect(client.pendingMfa, isNull);
      expect(
        () => client.completeMfa(const MfaAnswer.totp('123456')),
        throwsStateError,
      );
      expect(server.paths, hasLength(2));
    },
  );

  test(
    'verifyMfa steps up as the user and stores the new access token',
    () async {
      await serve([
        json(tokens('stronger')),
      ], signedIn: AuthSession.fromJson(tokens('weaker')));
      final events = <AuthEvent>[];
      client.authStateChanges.listen((c) => events.add(c.event));

      await client.verifyMfa(const MfaAnswer.recoveryCode('AAAAA-BBBBB'));
      await Future<void>.delayed(Duration.zero);

      expect(server.paths, ['/v1/account/mfa/verify']);
      expect(server.requests[0].value('Authorization'), 'Bearer weaker');
      expect(jsonDecode(server.bodies[0]), {'recoveryCode': 'AAAAA-BBBBB'});
      expect((await client.session.read())?.accessToken, 'stronger');
      expect(events, [AuthEvent.tokenRefreshed]);
    },
  );

  test(
    'confirmTotp returns the recovery codes and stores the new access token',
    () async {
      await serve([
        json({
          'recoveryCodes': ['AAAAA-BBBBB'],
          'session': tokens('level2'),
        }),
      ], signedIn: AuthSession.fromJson(tokens('level1')));

      expect(await client.confirmTotp('123456'), ['AAAAA-BBBBB']);
      expect(server.paths, ['/v1/account/mfa/totp/confirm']);
      expect((await client.session.read())?.accessToken, 'level2');
    },
  );
}
