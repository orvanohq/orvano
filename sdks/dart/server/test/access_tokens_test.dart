@TestOn('vm')
library;

import 'dart:convert';

import 'package:clock/clock.dart';
import 'package:dart_jsonwebtoken/dart_jsonwebtoken.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_dart/orvano_dart.dart';
import 'package:test/test.dart';

// Spec 0004 AC-19: verifyAccessToken checks a user's token locally against the
// project's JWKS, keeping the keys and fetching again (with no-cache, at most
// once per 30 seconds) for an unknown kid; online also asks Orvano as the
// user.

const endpoint = 'https://orvano.example.com';
const project = 'shop';

/// Three P-256 test keys: `d`, `x`, `y` as base64url.
const keys = {
  'a': (
    'JMa7UPOTgKDzSRGFkXT8feym3MmNba5J4iLiscvzO8M',
    '57COc6XPGDjN6sOVVdmMU4nmhfVcd_pNDxbkaMcXBKo',
    't7390MKY3SVeZ-SmnvRFxejTv1npg-Gbu8h9xX7aYXg',
  ),
  'b': (
    'Lo7cBK6-8Ravdwnn-g81Ii30w7ktXAiHXcQAyGbWeFw',
    'tncW6s5DgOy4t6Zx1uFW1WFAeBeOM4CB6cnzW3j7tqE',
    '5UipnBOIH37P0oaYbK2LSmMTg7nUsE7qoCfUA6RN79k',
  ),
  'c': (
    'EzVbdz7qe56I5bryoVJ5FkBSMbMWKU-3vPxsWZ5Jeh0',
    'cH-vbs7H1Ma4szb8IEwoUr4VL8DE4rp7pqP66dRKRCU',
    'xxZxEjliq1hIo7f58ecbPs-CrMJNUREAc14sDooyjec',
  ),
};

Map<String, Object?> publicJwk(String kid, String name) => {
  'kty': 'EC',
  'crv': 'P-256',
  'x': keys[name]!.$2,
  'y': keys[name]!.$3,
  'kid': kid,
  'alg': 'ES256',
  'use': 'sig',
};

/// A token signed with test key [name] under [kid].
String sign(
  String name, {
  String kid = 'k1',
  String audience = project,
  String issuer = '$endpoint/v1/projects/$project',
  String? sid = 'session-1',
  DateTime? expires,
  bool? emailVerified,
  Map<String, Object?> extra = const {},
}) {
  final key = JWTKey.fromJWK({...publicJwk(kid, name), 'd': keys[name]!.$1});
  final exp = expires ?? clock.now().add(const Duration(minutes: 15));
  return JWT(
    {
      'sub': 'user-1',
      'sid': ?sid,
      'email_verified': ?emailVerified,
      'iat': exp.millisecondsSinceEpoch ~/ 1000 - 900,
      'exp': exp.millisecondsSinceEpoch ~/ 1000,
      ...extra,
    },
    header: {'kid': kid},
    audience: Audience.one(audience),
    issuer: issuer,
  ).sign(key, algorithm: JWTAlgorithm.ES256, noIssueAt: true);
}

/// A client whose requests land in [sent]; [answer] answers each.
Client client(
  List<http.BaseRequest> sent,
  http.Response Function(http.Request) answer, {
  String? apiKey,
}) => Client(
  endpoint: endpoint,
  project: project,
  apiKey: apiKey,
  onWarning: (_) {},
  maxRetries: 0,
  httpClient: MockClient((request) async {
    sent.add(request);
    return answer(request);
  }),
);

http.Response jwks(List<Map<String, Object?>> list) => http.Response(
  jsonEncode({'keys': list}),
  200,
  headers: {'content-type': 'application/json'},
);

Matcher orvanoError(String code) => isA<OrvanoException>()
    .having((e) => e.status, 'status', 401)
    .having((e) => e.code, 'code', code);

void main() {
  test('verifies a token of the project and keeps the keys', () async {
    final sent = <http.BaseRequest>[];
    final c = client(sent, (_) => jwks([publicJwk('k1', 'a')]));

    final first = await c.verifyAccessToken(sign('a'));
    final second = await c.verifyAccessToken(sign('a'));

    expect(first.userId, 'user-1');
    expect(first.sessionId, 'session-1');
    expect(
      first.emailVerified,
      isFalse,
    ); // spec 0010 AC-14: missing reads as false
    expect(second.userId, 'user-1');
    expect(
      (await c.verifyAccessToken(sign('a', emailVerified: true))).emailVerified,
      isTrue,
    );
    expect(sent.single.url.path, '/v1/projects/shop/.well-known/jwks.json');
    expect(sent.single.headers.containsKey('Cache-Control'), isFalse);
  });

  test(
    'an expired token is token_expired after the 30 second leeway',
    () async {
      final c = client([], (_) => jwks([publicJwk('k1', 'a')]));
      final now = clock.now();

      final within = await c.verifyAccessToken(
        sign('a', expires: now.subtract(const Duration(seconds: 20))),
      );

      expect(within.userId, 'user-1');
      await expectLater(
        c.verifyAccessToken(
          sign('a', expires: now.subtract(const Duration(seconds: 40))),
        ),
        throwsA(orvanoError('token_expired')),
      );
    },
  );

  test('refuses another project, issuer, algorithm, or a forgery', () async {
    final c = client([], (_) => jwks([publicJwk('k1', 'a')]));
    final valid = sign('a');
    final parts = valid.split('.');
    String encode(Object json) =>
        base64Url.encode(utf8.encode(jsonEncode(json))).replaceAll('=', '');
    final none =
        '${encode({'alg': 'none', 'typ': 'JWT', 'kid': 'k1'})}.${parts[1]}.';
    final hs256 = JWT(
      {'sub': 'user-1', 'sid': 'session-1'},
      header: {'kid': 'k1'},
      audience: Audience.one(project),
      issuer: '$endpoint/v1/projects/$project',
    ).sign(SecretKey('secret'));

    for (final forged in [
      sign('a', audience: 'blog'),
      sign('a', issuer: '$endpoint/v1/projects/blog'),
      sign('a', issuer: 'https://elsewhere.example.com/v1/projects/shop'),
      sign('b'), // same kid, other key
      sign('a', sid: null),
      none,
      hs256,
      '${parts[0]}.${parts[1]}.${parts[2].substring(0, parts[2].length - 4)}AAAA',
      'not a token',
    ]) {
      await expectLater(
        c.verifyAccessToken(forged),
        throwsA(orvanoError('invalid_token')),
        reason: forged,
      );
    }
  });

  test(
    'an unknown kid fetches again with no-cache at most once per 30 s',
    () async {
      final sent = <http.BaseRequest>[];
      var calls = 0;
      final c = client(
        sent,
        (_) => jwks([
          publicJwk('old', 'a'),
          if (++calls > 1) publicJwk('new', 'b'),
        ]),
      );

      await c.verifyAccessToken(sign('a', kid: 'old'));
      final rotated = await c.verifyAccessToken(sign('b', kid: 'new'));

      expect(rotated.userId, 'user-1');
      await expectLater(
        c.verifyAccessToken(sign('c', kid: 'other')),
        throwsA(orvanoError('invalid_token')),
      );
      expect(sent, hasLength(2));
      expect(sent[1].headers['Cache-Control'], 'no-cache');
    },
  );

  test('online asks Orvano as the user without the API key', () async {
    final sent = <http.BaseRequest>[];
    var accountCalls = 0;
    final c = client(sent, apiKey: 'orv_sk_secret', (request) {
      if (request.url.path.endsWith('jwks.json')) {
        return jwks([publicJwk('k1', 'a')]);
      }
      return ++accountCalls == 1
          ? http.Response(
              '{"id":"user-1","email":null,"emailVerified":false,"name":null,'
              '"status":"active","metadata":{},'
              '"createdAt":"2026-01-01T00:00:00Z","lastSignInAt":null}',
              200,
              headers: {'content-type': 'application/json'},
            )
          : http.Response(
              '{"status":401,"code":"invalid_token","detail":"ended"}',
              401,
              headers: {'content-type': 'application/problem+json'},
            );
    });
    final token = sign('a');

    final verified = await c.verifyAccessToken(token, online: true);

    expect(verified.sessionId, 'session-1');
    final check = sent[1];
    expect(check.url.path, '/v1/account');
    expect(check.headers['Authorization'], 'Bearer $token');
    expect(check.headers['X-Orvano-Project'], project);
    expect(check.headers.containsKey('X-Orvano-Key'), isFalse);
    await expectLater(
      c.verifyAccessToken(token, online: true),
      throwsA(orvanoError('invalid_token')),
    );
  });

  // Spec 0013 AC-39: aal and amr from the token, missing ones read as 1 and
  // empty; requireMfa refuses a one factor session before any online check.
  test('reads aal and amr, and requireMfa refuses one factor', () async {
    final sent = <http.BaseRequest>[];
    final c = client(sent, (_) => jwks([publicJwk('k1', 'a')]));
    final strong = sign(
      'a',
      extra: {
        'aal': 2,
        'amr': ['mfa', 'otp', 'pwd'],
      },
    );
    final weak = sign(
      'a',
      extra: {
        'aal': 1,
        'amr': ['pwd'],
      },
    );
    final old = sign('a');
    final junk = sign('a', extra: {'aal': 'two', 'amr': 'pwd'});

    final verified = await c.verifyAccessToken(strong, requireMfa: true);

    expect(verified.aal, 2);
    expect(verified.amr, ['mfa', 'otp', 'pwd']);
    expect((await c.verifyAccessToken(weak)).amr, ['pwd']);
    expect((await c.verifyAccessToken(old)).aal, 1);
    expect((await c.verifyAccessToken(old)).amr, isEmpty);
    expect((await c.verifyAccessToken(junk)).aal, 1);
    expect((await c.verifyAccessToken(junk)).amr, isEmpty);
    for (final token in [weak, old]) {
      await expectLater(
        c.verifyAccessToken(token, requireMfa: true, online: true),
        throwsA(
          isA<OrvanoException>()
              .having((e) => e.status, 'status', 403)
              .having((e) => e.code, 'code', 'mfa_required'),
        ),
      );
    }
    expect(sent, hasLength(1));
  });

  test('needs a project', () {
    final c = Client(endpoint: endpoint, onWarning: (_) {});

    expect(() => c.verifyAccessToken('x'), throwsStateError);
  });
}
