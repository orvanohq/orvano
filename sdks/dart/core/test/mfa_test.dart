@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0013 AC-38: a sign in that stops at the MFA step stores nothing,
// keeps the ticket in memory, and emits mfaRequired; completeMfa finishes it;
// verifyMfa and confirmTotp store the new access token and keep the refresh
// token the store holds, since Orvano sends none (a RaisedSession).

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

Map<String, Object?> raised(String access, {String sid = 's1'}) => {
  'accessToken': access,
  'accessTokenExpiresAt': DateTime.now()
      .toUtc()
      .add(const Duration(minutes: 15))
      .toIso8601String(),
  'sessionId': sid,
};

final kept = AuthSession(
  accessToken: 'weaker',
  accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 5)),
  refreshToken: 'orv_rt_kept',
  refreshTokenExpiresAt: DateTime.utc(2030, 1, 2, 3, 4, 5),
  sessionId: 's1',
);

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
    'enrollmentRequired': false,
  },
  'isNewUser': false,
  'verificationEmail': null,
  'verificationRequired': false,
}, status: 201);

Answer signedIn() => json({
  'user': user,
  'session': tokens('level2', sid: 's2'),
  'mfa': null,
  'isNewUser': false,
  'verificationEmail': null,
  'verificationRequired': false,
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

  test('verifyMfa steps up as the user, stores the new access token, and keeps '
      'the refresh token', () async {
    await serve([json(raised('stronger'))], signedIn: kept);
    final events = <AuthEvent>[];
    client.authStateChanges.listen((c) => events.add(c.event));

    await client.verifyMfa(const MfaAnswer.recoveryCode('AAAAA-BBBBB'));
    await Future<void>.delayed(Duration.zero);

    expect(server.paths, ['/v1/account/mfa/verify']);
    expect(server.requests[0].value('Authorization'), 'Bearer weaker');
    expect(jsonDecode(server.bodies[0]), {'recoveryCode': 'AAAAA-BBBBB'});
    final after = await client.session.read();
    expect(after?.accessToken, 'stronger');
    expect(after?.refreshToken, 'orv_rt_kept');
    expect(after?.refreshTokenExpiresAt, kept.refreshTokenExpiresAt);
    expect(after?.sessionId, 's1');
    expect(events, [AuthEvent.tokenRefreshed]);
  });

  test(
    'confirmTotp returns the recovery codes, stores the new access token, and '
    'keeps the refresh token',
    () async {
      await serve([
        json({
          'recoveryCodes': ['AAAAA-BBBBB'],
          'session': raised('level2'),
        }),
      ], signedIn: kept);
      final events = <AuthEvent>[];
      client.authStateChanges.listen((c) => events.add(c.event));

      expect(await client.confirmTotp('123456'), ['AAAAA-BBBBB']);
      await Future<void>.delayed(Duration.zero);

      expect(server.paths, ['/v1/account/mfa/totp/confirm']);
      expect(jsonDecode(server.bodies[0]), {'code': '123456'});
      final after = await client.session.read();
      expect(after?.accessToken, 'level2');
      expect(after?.refreshToken, 'orv_rt_kept');
      expect(after?.refreshTokenExpiresAt, kept.refreshTokenExpiresAt);
      expect(events, [AuthEvent.tokenRefreshed]);
    },
  );

  test('createTotp sends the current password in its body', () async {
    await serve([
      json({
        'secret': 'S',
        'uri': 'otpauth://totp/x',
        'expiresAt': '2026-10-07T12:05:00Z',
      }, status: 201),
    ], signedIn: kept);

    await Orvano(
      client,
    ).account.createTotp(const CreateTotpRequest(password: 'correct horse'));

    expect(server.paths, ['/v1/account/mfa/totp']);
    expect(jsonDecode(server.bodies[0]), {'password': 'correct horse'});
  });

  test(
    'registerPasskey sends the current password to start the registration',
    () async {
      await serve([
        json({
          'challengeId': 'reg1',
          'options': {
            'rp': {'id': 'example.com', 'name': 'Acme'},
            'user': {
              'id': 'dTE',
              'name': 'ada@example.com',
              'displayName': 'Ada',
            },
            'challenge': 'Y2hhbGxlbmdl',
            'pubKeyCredParams': [
              {'type': 'public-key', 'alg': -7},
            ],
            'timeout': 300000,
            'excludeCredentials': <Object>[],
            'authenticatorSelection': {
              'residentKey': 'required',
              'requireResidentKey': true,
              'userVerification': 'required',
            },
            'attestation': 'none',
          },
        }),
      ], signedIn: kept);

      await expectLater(
        client.registerPasskey(
          password: 'correct horse',
          authenticator: const _NoPasskeys(),
        ),
        throwsA(isA<_Stopped>()),
      );

      expect(server.paths, ['/v1/account/passkeys/registration']);
      expect(jsonDecode(server.bodies[0]), {'password': 'correct horse'});
    },
  );

  // Spec 0014 AC-27, AC-36: a project that requires MFA answers a user with no
  // factor an enrollment challenge; the helpers spend its ticket and store the
  // session the first factor earns.
  Answer mustEnroll() => json({
    'user': null,
    'session': null,
    'mfa': {
      'ticket': ticket,
      'factors': ['totp', 'passkey'],
      'expiresAt': '2026-10-07T12:20:00Z',
      'enrollmentRequired': true,
    },
    'isNewUser': false,
    'verificationEmail': null,
    'verificationRequired': false,
  }, status: 201);

  test(
    'an enrollment challenge refuses completeMfa and enrolls TOTP with the ticket',
    () async {
      await serve([
        mustEnroll(),
        json({
          'secret': 'JBSWY3DPEHPK3PXP',
          'uri': 'otpauth://totp/x',
          'expiresAt': '2026-10-07T12:20:00Z',
        }, status: 201),
        json({
          'auth': {
            'user': user,
            'session': tokens('level2', sid: 's9'),
            'mfa': null,
            'isNewUser': false,
            'verificationEmail': null,
            'verificationRequired': false,
          },
          'recoveryCodes': ['AAAAA-BBBBB'],
        }, status: 201),
      ]);
      final events = <AuthEvent>[];
      client.authStateChanges.listen((c) => events.add(c.event));

      final result = await Orvano(client).account.createPasswordSession(
        const CreatePasswordSessionRequest(
          email: 'ada@example.com',
          password: 'pw',
        ),
      );
      expect(enrollmentRequired(result), isTrue);
      expect(client.pendingMfa?.enrollmentRequired, isTrue);
      expect(
        () => client.completeMfa(const MfaAnswer.totp('123456')),
        throwsStateError,
      );

      expect((await client.startTotpEnrollment()).secret, 'JBSWY3DPEHPK3PXP');
      final enrolled = await client.completeTotpEnrollment('123456');
      await Future<void>.delayed(Duration.zero);

      expect(enrolled.user.id, 'u1');
      expect(enrolled.recoveryCodes, ['AAAAA-BBBBB']);
      expect(server.paths.sublist(1), [
        '/v1/account/mfa/enrollment/totp',
        '/v1/account/mfa/enrollment/totp/confirm',
      ]);
      expect(server.jsonBody(2), {'ticket': ticket, 'code': '123456'});
      expect((await client.session.read())?.sessionId, 's9');
      expect(client.pendingMfa, isNull);
      expect(events, [AuthEvent.mfaRequired, AuthEvent.signedIn]);
    },
  );

  test('an ended enrollment ticket is forgotten', () async {
    await serve([
      mustEnroll(),
      json({
        'status': 401,
        'code': 'invalid_mfa_ticket',
        'title': 'Unauthorized',
      }, status: 401),
    ]);
    await signIn();

    await expectLater(
      client.startTotpEnrollment(),
      throwsA(isA<OrvanoException>()),
    );
    expect(client.pendingMfa, isNull);
    expect(client.startTotpEnrollment, throwsStateError);
  });
}

/// Thrown by [_NoPasskeys] to stop a ceremony after the first call.
final class _Stopped implements Exception {
  const _Stopped();
}

/// A passkey authenticator that stops every ceremony, for tests that only
/// check what the first call sent.
final class _NoPasskeys implements PasskeyAuthenticator {
  const _NoPasskeys();

  @override
  Future<PasskeyRegistrationCredential> create(
    PasskeyCreationOptions options,
  ) => throw const _Stopped();

  @override
  Future<PasskeyAssertionCredential> get(
    PasskeyRequestOptions options, {
    bool autofill = false,
  }) => throw const _Stopped();
}
