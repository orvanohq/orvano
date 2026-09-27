@TestOn('vm')
library;

import 'dart:convert';
import 'dart:io';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

// Spec 0004 AC-26: refresh before a call, one retry after a stale token, the
// failure policy, and the auth state stream.

AuthSession session(String token, Duration left, {String sid = 's1'}) =>
    AuthSession(
      accessToken: token,
      accessTokenExpiresAt: DateTime.now().toUtc().add(left),
      refreshToken: 'orv_rt_$token',
      refreshTokenExpiresAt: DateTime.now().toUtc().add(
        const Duration(days: 30),
      ),
      sessionId: sid,
    );

/// A refresh answer carrying fresh tokens for [token].
Answer tokens(String token) => (response) async {
  response.headers.contentType = ContentType.json;
  response.write(
    jsonEncode(session(token, const Duration(minutes: 15)).toJson()),
  );
  await response.close();
};

Answer json(Object body) => (response) async {
  response.headers.contentType = ContentType.json;
  response.write(jsonEncode(body));
  await response.close();
};

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

  group('refresh before a call', () {
    test(
      'refreshes when under 60 seconds are left, then sends the new token',
      () async {
        await serve([
          tokens('a2'),
          health(),
        ], signedIn: session('a1', const Duration(seconds: 30)));
        final events = <AuthEvent>[];
        client.authStateChanges.listen((c) => events.add(c.event));

        await Orvano(client).health.get();
        await Future<void>.delayed(Duration.zero);

        expect(server.paths, ['/v1/account/sessions/refresh', '/v1/health']);
        expect(server.requests[0].value('Authorization'), isNull);
        expect(server.requests[1].value('Authorization'), 'Bearer a2');
        expect(events, [AuthEvent.tokenRefreshed]);
      },
    );

    test('does not refresh while more than 60 seconds are left', () async {
      await serve([
        health(),
      ], signedIn: session('a1', const Duration(minutes: 2)));

      await Orvano(client).health.get();

      expect(server.paths, ['/v1/health']);
    });

    test('refreshes once for calls made at the same time', () async {
      await serve([
        tokens('a2'),
        health(),
      ], signedIn: session('a1', const Duration(seconds: 5)));

      await Future.wait([
        Orvano(client).health.get(),
        Orvano(client).health.get(),
      ]);

      expect(server.paths.where((p) => p.endsWith('/refresh')), hasLength(1));
    });
  });

  group('after a 401', () {
    test('refreshes once and repeats the call once on token_expired', () async {
      await serve([
        problem(401, {'code': 'token_expired'}),
        tokens('a2'),
        health(),
      ], signedIn: session('a1', const Duration(minutes: 10)));

      await Orvano(client).health.get();

      expect(server.requests.map((h) => h.value('Authorization')), [
        'Bearer a1',
        null,
        'Bearer a2',
      ]);
    });

    test(
      'clears the session and says signedOut when the refresh is refused',
      () async {
        await serve([
          problem(401, {'code': 'invalid_token'}),
          problem(401, {'code': 'invalid_refresh_token'}),
        ], signedIn: session('a1', const Duration(minutes: 10)));
        final events = <AuthEvent>[];
        client.authStateChanges.listen((c) => events.add(c.event));

        await expectLater(
          Orvano(client).health.get(),
          throwsA(
            isA<OrvanoException>().having(
              (e) => e.code,
              'code',
              'invalid_token',
            ),
          ),
        );
        await Future<void>.delayed(Duration.zero);

        expect(await client.session.read(), isNull);
        expect(events, [AuthEvent.signedOut]);
      },
    );

    test('never refreshes after a 401 for anything else', () async {
      await serve([
        problem(401, {'code': 'session_required'}),
      ], signedIn: session('a1', const Duration(minutes: 10)));

      await expectLater(
        Orvano(client).health.get(),
        throwsA(isA<OrvanoException>()),
      );

      expect(server.paths, hasLength(1));
    });
  });

  test('a network error during the refresh keeps the session', () async {
    await serve([
      health(),
    ], signedIn: session('a1', const Duration(seconds: 5)));
    final offline = Client(
      endpoint: 'http://127.0.0.1:1',
      project: 'shop',
      session: MemorySessionStore(session('a1', const Duration(seconds: 5))),
      maxRetries: 0,
      onWarning: (_) {},
    );

    expect((await offline.getSession())?.accessToken, 'a1');
    offline.close();
  });

  test('the stream says signedIn, userUpdated, and signedOut as the contract '
      'marks the calls', () async {
    final signedIn = session('a1', const Duration(minutes: 15));
    await serve([
      json({
        'user': {'id': 'u'},
        'session': signedIn.toJson(),
      }),
      json({'id': 'u'}),
      status(204),
    ]);
    final seen = <(AuthEvent, String?)>[];
    client.authStateChanges.listen(
      (c) => seen.add((c.event, c.session?.accessToken)),
    );

    await client.send(
      'POST',
      '/v1/account/sessions/password',
      session: SessionChange.start,
    );
    await client.send('PATCH', '/v1/account', session: SessionChange.user);
    await client.send(
      'DELETE',
      '/v1/account/sessions/current',
      session: SessionChange.end,
    );
    await Future<void>.delayed(Duration.zero);

    expect(seen, [
      (AuthEvent.signedIn, 'a1'),
      (AuthEvent.userUpdated, 'a1'),
      (AuthEvent.signedOut, null),
    ]);
  });
}
