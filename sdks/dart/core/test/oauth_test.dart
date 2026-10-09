@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';
import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0012 AC-22 (with AC-20's shared rules): provider sign in and linking
// by redirect through a launcher, the redirect handler, native ID tokens, and
// OrvanoNonce.

final redirect = Uri.parse('com.acme.app://auth');
final providerUrl = 'https://accounts.google.com/o/oauth2/v2/auth?state=s';
final code = 'orv_oc_${'B' * 43}';
final base64UrlChars = RegExp(r'^[A-Za-z0-9_-]+$');

/// An unsigned JWT with the claims the client reads.
String jwt(Map<String, Object?> claims) {
  String part(Object value) =>
      base64Url.encode(utf8.encode(jsonEncode(value))).replaceAll('=', '');
  return '${part({'alg': 'ES256'})}.${part(claims)}.sig';
}

AuthSession session(String sub, {String sid = 's1'}) => AuthSession(
  accessToken: jwt({'sub': sub, 'sid': sid, 'email_verified': true}),
  accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 15)),
  refreshToken: 'orv_rt_x',
  refreshTokenExpiresAt: DateTime.now().toUtc().add(const Duration(days: 30)),
  sessionId: sid,
);

Map<String, Object?> user(String id) => {
  'id': id,
  'email': 'ada@example.com',
  'emailVerified': true,
  'emailVerifiedAt': '2026-10-01T12:00:00Z',
  'name': null,
  'status': 'active',
  'metadata': <String, Object?>{},
  'createdAt': '2026-10-01T12:00:00Z',
  'lastSignInAt': null,
  'providers': <String>['google'],
  'hasPassword': false,
  'mfaEnabled': false,
};

Map<String, Object?> identity(String provider) => {
  'id': 'i-$provider',
  'provider': provider,
  'subject': '5150',
  'email': 'gh@x.com',
  'emailVerified': true,
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

Answer flow() => json({'url': providerUrl});

Answer authResult(String sub, {bool isNewUser = false, String sid = 's2'}) =>
    json({
      'user': user(sub),
      'session': session(sub, sid: sid).toJson(),
      'isNewUser': isNewUser,
      'verificationEmail': null,
    }, code: 201);

String s256(String verifier) => base64Url
    .encode(sha256.convert(ascii.encode(verifier)).bytes)
    .replaceAll('=', '');

/// A launcher that records what it was asked to open and returns [back].
final class RecordingLauncher {
  RecordingLauncher(this.back);

  final Uri back;
  final opened = <Uri>[];
  final redirects = <Uri>[];

  Future<Uri> call(Uri url, Uri redirectUrl) async {
    opened.add(url);
    redirects.add(redirectUrl);
    return back;
  }
}

Uri back(String type, {String? error}) => redirect.replace(
  queryParameters: {
    'orvano_type': type,
    if (error == null) 'orvano_code': code else 'orvano_error': error,
  },
);

void main() {
  group('OrvanoNonce.create (AC-22)', () {
    test('hashes the raw value to lowercase hex SHA-256', () {
      final nonce = OrvanoNonce.create();

      expect(nonce.raw, hasLength(43));
      expect(nonce.raw, matches(base64UrlChars));
      expect(nonce.hashed, sha256.convert(utf8.encode(nonce.raw)).toString());
      expect(nonce.hashed, matches(RegExp(r'^[0-9a-f]{64}$')));
    });

    test('makes a new value every time', () {
      expect(OrvanoNonce.create().raw, isNot(OrvanoNonce.create().raw));
    });
  });

  group('oauthRedirectError (AC-23)', () {
    test('gives each redirect error code its catalog status', () {
      expect(oauthRedirectError('oauth_access_denied').status, 403);
      expect(oauthRedirectError('provider_error').status, 502);
      expect(oauthRedirectError('provider_unavailable').status, 503);
      expect(oauthRedirectError('provider_not_enabled').status, 409);
      expect(oauthRedirectError('provider_not_configured').status, 409);
      expect(oauthRedirectError('something_new').status, 400);
      expect(oauthRedirectError('provider_error').code, 'provider_error');
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

    group('signInWithOAuth', () {
      test('starts with an S256 challenge, opens the provider, and redeems the '
          'code with the matching verifier', () async {
        await serve([flow(), authResult('u2', isNewUser: true)]);
        final launcher = RecordingLauncher(back('oauth'));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        final result = await client.signInWithOAuth(
          OAuthProvider.google,
          redirectUrl: redirect,
          launcher: launcher.call,
        );
        await Future<void>.delayed(Duration.zero);

        expect(server.paths, [
          '/v1/account/oauth/flows',
          '/v1/account/sessions/oauth',
        ]);
        final start = server.jsonBody(0);
        expect(start['provider'], 'google');
        expect(start['redirectUrl'], redirect.toString());
        final challenge = start['codeChallenge']! as String;
        expect(challenge, hasLength(43));
        expect(challenge, matches(base64UrlChars));
        expect(launcher.opened, [Uri.parse(providerUrl)]);
        expect(launcher.redirects, [redirect]);
        final redeem = server.jsonBody(1);
        expect(redeem['code'], code);
        final verifier = redeem['codeVerifier']! as String;
        expect(verifier, hasLength(43));
        expect(s256(verifier), challenge);
        expect(result.isNewUser, isTrue);
        expect(result.user?.id, 'u2');
        expect((await client.session.read())?.sessionId, 's2');
        expect(seen, [AuthEvent.signedIn]);
      });

      test('makes a new verifier for every flow', () async {
        await serve([
          flow(),
          authResult('u2'),
          flow(),
          authResult('u2', sid: 's3'),
        ]);
        final launcher = RecordingLauncher(back('oauth'));

        for (var i = 0; i < 2; i++) {
          await client.signInWithOAuth(
            OAuthProvider.github,
            redirectUrl: redirect,
            launcher: launcher.call,
          );
        }

        expect(
          server.jsonBody(0)['codeChallenge'],
          isNot(server.jsonBody(2)['codeChallenge']),
        );
      });

      test('uses the client default launcher when none is passed', () async {
        await serve([flow(), authResult('u2')]);
        final launcher = RecordingLauncher(back('oauth'));
        setDefaultOAuthLauncher(client, launcher.call);

        await client.signInWithOAuth(
          OAuthProvider.microsoft,
          redirectUrl: redirect,
        );

        expect(launcher.opened, hasLength(1));
      });

      test('throws before any call when there is no launcher', () async {
        await serve([flow()]);

        await expectLater(
          client.signInWithOAuth(OAuthProvider.google, redirectUrl: redirect),
          throwsArgumentError,
        );
        expect(server.paths, isEmpty);
      });

      test(
        'a refused start throws the typed error and opens nothing',
        () async {
          await serve([
            problem(409, {
              'type': 'https://orvano.dev/errors/provider_not_enabled',
              'title': 'Provider not enabled',
              'status': 409,
              'code': 'provider_not_enabled',
              'detail': 'Turn the provider on first.',
            }),
          ]);
          final launcher = RecordingLauncher(back('oauth'));

          await expectLater(
            client.signInWithOAuth(
              OAuthProvider.apple,
              redirectUrl: redirect,
              launcher: launcher.call,
            ),
            throwsA(
              isA<OrvanoException>().having(
                (e) => e.code,
                'code',
                'provider_not_enabled',
              ),
            ),
          );
          expect(launcher.opened, isEmpty);
        },
      );

      test('an orvano_error redirect throws it as the typed error, calls '
          'nothing more, and forgets the verifier', () async {
        await serve([flow(), authResult('u2')]);
        final launcher = RecordingLauncher(
          back('oauth', error: 'oauth_access_denied'),
        );

        await expectLater(
          client.signInWithOAuth(
            OAuthProvider.google,
            redirectUrl: redirect,
            launcher: launcher.call,
          ),
          throwsA(
            isA<OrvanoException>()
                .having((e) => e.code, 'code', 'oauth_access_denied')
                .having((e) => e.status, 'status', 403),
          ),
        );
        expect(server.paths, ['/v1/account/oauth/flows']);
        await expectLater(
          client.handleOAuthRedirect(back('oauth')),
          throwsArgumentError,
        );
        expect(server.paths, ['/v1/account/oauth/flows']);
      });

      test(
        'a launcher that returns some other URL is an argument error',
        () async {
          await serve([flow()]);
          final launcher = RecordingLauncher(
            Uri.parse('com.acme.app://auth?foo=bar'),
          );

          await expectLater(
            client.signInWithOAuth(
              OAuthProvider.google,
              redirectUrl: redirect,
              launcher: launcher.call,
            ),
            throwsArgumentError,
          );
        },
      );
    });

    group('linkIdentity', () {
      test('starts a link flow and completes it with the verifier, emitting '
          'userUpdated', () async {
        await serve([
          flow(),
          json(identity('github'), code: 201),
        ], signedIn: session('u1'));
        final launcher = RecordingLauncher(back('oauth_link'));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        final result = await client.linkIdentity(
          OAuthProvider.github,
          redirectUrl: redirect,
          launcher: launcher.call,
        );
        await Future<void>.delayed(Duration.zero);

        expect(server.paths, [
          '/v1/account/identities/oauth/flows',
          '/v1/account/identities/oauth',
        ]);
        expect(
          s256(server.jsonBody(1)['codeVerifier']! as String),
          server.jsonBody(0)['codeChallenge'],
        );
        expect(result.identity.provider, OAuthProvider.github);
        expect(result.isNewUser, isFalse);
        expect((await client.session.read())?.sessionId, 's1');
        expect(seen, [AuthEvent.userUpdated]);
        expect(server.jsonBody(0).containsKey('password'), isFalse);
      });

      test('sends the current password to start the link flow only', () async {
        await serve([
          flow(),
          json(identity('github'), code: 201),
        ], signedIn: session('u1'));

        await client.linkIdentity(
          OAuthProvider.github,
          redirectUrl: redirect,
          password: 'correct horse',
          launcher: RecordingLauncher(back('oauth_link')).call,
        );

        expect(server.jsonBody(0)['password'], 'correct horse');
        expect(server.jsonBody(1).containsKey('password'), isFalse);
      });
    });

    group('handleLink and handleOAuthRedirect', () {
      test('are null for a URL that is no provider redirect', () async {
        await serve([json({})]);

        expect(
          await client.handleOAuthRedirect(
            Uri.parse('https://a.example/?orvano_type=magic_link'),
          ),
          isNull,
        );
        expect(
          await client.handleOAuthRedirect(Uri.parse('https://a.example/')),
          isNull,
        );
        expect(server.paths, isEmpty);
      });

      test('a code with no stored verifier throws before any call', () async {
        await serve([authResult('u2')]);

        await expectLater(
          client.handleLink(back('oauth')),
          throwsArgumentError,
        );
        expect(server.paths, isEmpty);
      });

      test('a provider redirect with no code throws before any call', () async {
        await serve([authResult('u2')]);

        await expectLater(
          client.handleOAuthRedirect(
            redirect.replace(queryParameters: {'orvano_type': 'oauth'}),
          ),
          throwsArgumentError,
        );
        expect(server.paths, isEmpty);
      });

      test('the verifier works for one redemption only', () async {
        await serve([flow(), authResult('u2')]);
        final launcher = RecordingLauncher(back('oauth'));
        await client.signInWithOAuth(
          OAuthProvider.google,
          redirectUrl: redirect,
          launcher: launcher.call,
        );

        await expectLater(
          client.handleLink(back('oauth')),
          throwsArgumentError,
        );
        expect(server.paths, hasLength(2));
      });
    });

    group('native ID tokens', () {
      test(
        'signInWithIdToken sends the raw nonce and Apple code, and stores the '
        'session',
        () async {
          await serve([authResult('u3', isNewUser: true, sid: 's9')]);
          final nonce = OrvanoNonce.create();
          final seen = <AuthEvent>[];
          client.authStateChanges.listen((c) => seen.add(c.event));

          final result = await client.signInWithIdToken(
            provider: IdTokenProvider.apple,
            idToken: 'id.token.value',
            nonce: nonce.raw,
            authorizationCode: 'apple-code',
            name: 'Grace Hopper',
          );
          await Future<void>.delayed(Duration.zero);

          expect(server.paths, ['/v1/account/sessions/id-token']);
          expect(server.jsonBody(0), {
            'provider': 'apple',
            'idToken': 'id.token.value',
            'nonce': nonce.raw,
            'authorizationCode': 'apple-code',
            'name': 'Grace Hopper',
          });
          expect(result.isNewUser, isTrue);
          expect((await client.session.read())?.sessionId, 's9');
          expect(seen, [AuthEvent.signedIn]);
        },
      );

      test('linkIdentityWithIdToken links and emits userUpdated, keeping the '
          'session', () async {
        await serve([
          json(identity('google'), code: 201),
        ], signedIn: session('u1'));
        final seen = <AuthEvent>[];
        client.authStateChanges.listen((c) => seen.add(c.event));

        final linked = await client.linkIdentityWithIdToken(
          provider: IdTokenProvider.google,
          idToken: 'id.token.value',
          nonce: OrvanoNonce.create().raw,
        );
        await Future<void>.delayed(Duration.zero);

        expect(server.paths, ['/v1/account/identities/id-token']);
        expect(server.jsonBody(0)['provider'], 'google');
        expect(server.jsonBody(0).containsKey('password'), isFalse);
        expect(linked.provider, OAuthProvider.google);
        expect((await client.session.read())?.sessionId, 's1');
        expect(seen, [AuthEvent.userUpdated]);
      });

      test('linkIdentityWithIdToken sends the current password', () async {
        await serve([
          json(identity('google'), code: 201),
        ], signedIn: session('u1'));

        await client.linkIdentityWithIdToken(
          provider: IdTokenProvider.google,
          idToken: 'id.token.value',
          nonce: OrvanoNonce.create().raw,
          password: 'correct horse',
        );

        expect(server.jsonBody(0)['password'], 'correct horse');
      });

      test(
        'a refused token throws invalid_id_token and keeps the session',
        () async {
          await serve([
            problem(401, {
              'type': 'https://orvano.dev/errors/invalid_id_token',
              'title': 'Invalid ID token',
              'status': 401,
              'code': 'invalid_id_token',
              'detail': 'The ID token was refused.',
            }),
          ], signedIn: session('u1'));

          await expectLater(
            client.signInWithIdToken(
              provider: IdTokenProvider.google,
              idToken: 'id.token.value',
              nonce: OrvanoNonce.create().raw,
            ),
            throwsA(
              isA<OrvanoException>().having(
                (e) => e.code,
                'code',
                'invalid_id_token',
              ),
            ),
          );
          expect((await client.session.read())?.sessionId, 's1');
        },
      );
    });
  });
}
