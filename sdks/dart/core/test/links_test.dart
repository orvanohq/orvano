@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0010 AC-14, AC-24, AC-26: the link helper, the claim refresh, and
// retryAfter on the exception.

final token = 'orv_el_${'A' * 43}';

/// An unsigned JWT with the claims the client reads.
String jwt(Map<String, Object?> claims) {
  String part(Object value) =>
      base64Url.encode(utf8.encode(jsonEncode(value))).replaceAll('=', '');
  return '${part({'alg': 'ES256'})}.${part(claims)}.sig';
}

AuthSession session(
  String sub,
  bool verified, {
  String sid = 's1',
}) => AuthSession(
  accessToken: jwt({'sub': sub, 'sid': sid, 'email_verified': verified}),
  accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 15)),
  refreshToken: 'orv_rt_x',
  refreshTokenExpiresAt: DateTime.now().toUtc().add(const Duration(days: 30)),
  sessionId: sid,
);

Map<String, Object?> user(String id, bool verified) => {
  'id': id,
  'email': 'ada@example.com',
  'emailVerified': verified,
  'emailVerifiedAt': verified ? '2026-10-01T12:00:00Z' : null,
  'name': null,
  'status': 'active',
  'metadata': <String, Object?>{},
  'createdAt': '2026-10-01T12:00:00Z',
  'lastSignInAt': null,
};

Answer json(Object body, {int code = 200}) => (response) async {
  response
    ..statusCode = code
    ..headers.contentType = ContentType.json
    ..write(jsonEncode(body));
  await response.close();
};

Answer authResult(String sub, {bool isNewUser = false, String sid = 's2'}) =>
    json({
      'user': user(sub, true),
      'session': session(sub, true, sid: sid).toJson(),
      'isNewUser': isNewUser,
      'verificationEmail': null,
    }, code: 201);

void main() {
  group('readEmailLink', () {
    test('is null for a URL with neither parameter', () {
      expect(readEmailLink(Uri.parse('https://app.example.com/cb')), isNull);
    });

    test('throws for an unknown type, a missing token, or a reset without '
        'a password', () {
      expect(
        () => readEmailLink(
          Uri.parse('https://a.example/?orvano_type=foo&orvano_token=$token'),
        ),
        throwsArgumentError,
      );
      expect(
        () => readEmailLink(
          Uri.parse('https://a.example/?orvano_type=magic_link'),
        ),
        throwsArgumentError,
      );
      expect(
        () => readEmailLink(
          Uri.parse(
            'https://a.example/?orvano_type=recovery&orvano_token=$token',
          ),
        ),
        throwsArgumentError,
      );
      final reset = readEmailLink(
        Uri.parse(
          'com.acme.app://auth?orvano_type=recovery&orvano_token=$token',
        ),
        password: 'pw',
      );
      expect(reset?.type, EmailLinkType.recovery);
      expect(reset?.password, 'pw');
    });
  });

  group('with a fake server', () {
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

    group('handleLink (AC-24)', () {
      test(
        'returns null and calls nothing for a URL without the parameters',
        () async {
          await serve([json({})]);

          expect(
            await client.handleLink(Uri.parse('https://a.example/cb')),
            isNull,
          );
          expect(server.paths, isEmpty);
        },
      );

      test('a magic link stores the new session and emits signedIn', () async {
        await serve([
          authResult('u2', isNewUser: true),
        ], signedIn: session('u1', false));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        final result = await client.handleLink(
          Uri.parse(
            'https://a.example/cb?orvano_type=magic_link&orvano_token=$token',
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(result?.type, EmailLinkType.magicLink);
        expect(result?.isNewUser, isTrue);
        expect(result?.user.id, 'u2');
        expect((await client.session.read())?.sessionId, 's2');
        expect(server.paths, ['/v1/account/sessions/magic-link']);
        expect(seen, [AuthEvent.signedIn]);
      });

      test('a verification for the signed in user refreshes and emits '
          'userUpdated', () async {
        await serve([
          json(user('u1', true)),
          json(session('u1', true).toJson()),
        ], signedIn: session('u1', false));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        final result = await client.handleLink(
          Uri.parse(
            'https://a.example/?orvano_type=verification&orvano_token=$token',
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(result?.type, EmailLinkType.verification);
        expect(server.paths, [
          '/v1/account/verification/confirm',
          '/v1/account/sessions/refresh',
        ]);
        expect(seen, [AuthEvent.tokenRefreshed, AuthEvent.userUpdated]);
      });

      test('an email change of another user changes nothing here', () async {
        await serve([
          json(user('someone-else', true)),
        ], signedIn: session('u1', false));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        await client.handleLink(
          Uri.parse(
            'https://a.example/?orvano_type=email_change&orvano_token=$token',
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(server.paths, ['/v1/account/email/confirm']);
        expect(seen, isEmpty);
      });

      test('a used link throws the refusal and keeps the session', () async {
        final before = session('u1', false);
        await serve([
          problem(401, {'code': 'invalid_email_token'}),
        ], signedIn: before);

        await expectLater(
          client.handleLink(
            Uri.parse(
              'https://a.example/?orvano_type=magic_link&orvano_token=$token',
            ),
          ),
          throwsA(
            isA<OrvanoException>().having(
              (e) => e.code,
              'code',
              'invalid_email_token',
            ),
          ),
        );
        expect((await client.session.read())?.accessToken, before.accessToken);
      });
    });

    group('retryAfter (AC-26)', () {
      test(
        'reads Retry-After seconds or a date, and null without one',
        () async {
          await serve([
            problem(429, {'code': 'rate_limited'}, retryAfter: '42'),
            problem(401, {'code': 'invalid_code'}),
          ]);

          final limited = await client
              .send('POST', '/v1/x')
              .then<OrvanoException?>(
                (_) => null,
                onError: (Object e) => e as OrvanoException,
              );
          final plain = await client
              .send('POST', '/v1/x')
              .then<OrvanoException?>(
                (_) => null,
                onError: (Object e) => e as OrvanoException,
              );

          expect(limited?.retryAfter, const Duration(seconds: 42));
          expect(plain?.retryAfter, isNull);
          final now = DateTime.utc(2026, 10, 1, 12);
          expect(
            OrvanoException.parseRetryAfter(
              'Thu, 01 Oct 2026 12:01:30 GMT',
              now: now,
            ),
            const Duration(seconds: 90),
          );
          expect(OrvanoException.parseRetryAfter('soon'), isNull);
        },
      );
    });
  });
}
