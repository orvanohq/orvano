import 'dart:async';

import 'package:orvano_core/orvano_core.dart';
import 'package:test/test.dart';

import 'fake_orvano.dart';

void main() {
  FakeOrvano? running;
  FakeOrvano server() => running!;
  final warnings = <String>[];

  Future<void> serve(List<Answer> answers) async =>
      running = await FakeOrvano.start(answers);

  Orvano app({
    String? endpoint,
    String? project,
    SessionStore? session,
    int maxRetries = 3,
    Duration? timeout,
  }) => Orvano(
    Client(
      endpoint: endpoint ?? server().endpoint,
      project: project,
      session: session,
      maxRetries: maxRetries,
      timeout: timeout ?? const Duration(seconds: 5),
      onWarning: warnings.add,
    ),
  );

  setUp(warnings.clear);
  tearDown(() async {
    await running?.close();
    running = null;
  });

  group('headers', () {
    test('sends the SDK name and version on every request (AC-11)', () async {
      await serve([health()]);

      await app().health.get();

      expect(
        server().requests.single.value('X-Orvano-SDK'),
        'orvano_core/$sdkVersion',
      );
    });

    test(
      'sends the project and the session token, and never an API key (AC-4)',
      () async {
        await serve([health()]);

        await app(project: 'p1', session: MemorySessionStore('t')).health.get();

        final headers = server().requests.single;
        expect(headers.value('X-Orvano-Project'), 'p1');
        expect(headers.value('X-Orvano-Session'), 't');
        expect(headers.value('X-Orvano-Key'), isNull);
      },
    );

    test('joins the endpoint and the operation path with one slash', () async {
      await serve([health()]);

      await app(endpoint: '${server().endpoint}/').health.get();

      expect(server().paths.single, '/v1/health');
    });

    test('refuses an endpoint that is not an absolute URL', () {
      expect(() => Client(endpoint: '/orvano'), throwsArgumentError);
    });
  });

  group('version warning (AC-11)', () {
    test('warns once per client when the server runs another minor', () async {
      await serve([health('0.99.0')]);
      final orvano = app();

      await orvano.health.get();
      await orvano.health.get();

      expect(warnings, hasLength(1));
      expect(
        warnings.single,
        allOf(contains('runs 0.99.0'), contains('orvano_core 0.99.x')),
      );
    });

    test('stays quiet when only the patch version differs', () async {
      final [major, minor, ...] = sdkVersion.split('.');
      await serve([health('$major.$minor.99')]);

      await app().health.get();

      expect(warnings, isEmpty);
    });
  });

  group('retries (AC-14)', () {
    for (final code in [503, 429]) {
      test('retries a GET after a $code, honoring Retry-After', () async {
        await serve([status(code, retryAfter: '0'), health()]);

        final result = await app().health.get();

        expect(result.status, 'ok');
        expect(server().requests, hasLength(2));
      });
    }

    test('gives up after maxRetries with the last status', () async {
      await serve([status(503, retryAfter: '0')]);

      await expectLater(
        app(maxRetries: 2).health.get(),
        throwsA(isA<OrvanoException>().having((e) => e.status, 'status', 503)),
      );
      expect(server().requests, hasLength(3));
    });

    test('does not retry other failures', () async {
      await serve([status(500)]);

      await expectLater(app().health.get(), throwsA(isA<OrvanoException>()));
      expect(server().requests, hasLength(1));
    });

    test('never retries a POST that is not marked idempotent', () async {
      await serve([status(503, retryAfter: '0')]);

      await expectLater(
        app().client.send('POST', '/v1/things'),
        throwsA(isA<OrvanoException>()),
      );
      expect(server().methods, ['POST']);
    });

    test('retries a POST marked idempotent', () async {
      await serve([status(503, retryAfter: '0'), status(204)]);

      await app().client.send('POST', '/v1/things', idempotent: true);

      expect(server().methods, ['POST', 'POST']);
    });
  });

  group('timeouts (AC-14)', () {
    test('times out a call that takes too long', () async {
      await serve([hang()]);

      await expectLater(
        app().health.get(
          options: const RequestOptions(timeout: Duration(milliseconds: 100)),
        ),
        throwsA(isA<TimeoutException>()),
      );
    });

    test('covers retry waits with the same timeout', () async {
      await serve([status(503, retryAfter: '30')]);
      final watch = Stopwatch()..start();

      await expectLater(
        app(timeout: const Duration(milliseconds: 200)).health.get(),
        throwsA(isA<TimeoutException>()),
      );
      expect(watch.elapsed, lessThan(const Duration(seconds: 5)));
    });
  });

  group('errors (AC-6)', () {
    test(
      'maps a problem body to one exception with its code, detail, and request ID',
      () async {
        await serve([
          problem(409, {
            'title': 'Conflict',
            'status': 409,
            'detail': 'Already there.',
            'code': 'user_already_exists',
            'requestId': 'body-id',
          }, requestId: 'header-id'),
        ]);

        await expectLater(
          app().health.get(),
          throwsA(
            isA<OrvanoException>()
                .having((e) => e.status, 'status', 409)
                .having((e) => e.code, 'code', 'user_already_exists')
                .having((e) => e.message, 'message', 'Already there.')
                .having((e) => e.requestId, 'requestId', 'body-id'),
          ),
        );
      },
    );

    test('falls back to the title when a problem has no detail', () async {
      await serve([
        problem(404, {
          'title': 'Not Found',
          'status': 404,
          'code': 'not_found',
          'requestId': 'r',
        }),
      ]);

      await expectLater(
        app().health.get(),
        throwsA(
          isA<OrvanoException>().having(
            (e) => e.message,
            'message',
            'Not Found',
          ),
        ),
      );
    });

    test(
      'uses unknown and the request ID header when the body is not a problem',
      () async {
        await serve([
          (response) async {
            response
              ..statusCode = 502
              ..headers.set('X-Request-Id', 'header-id')
              ..write('<html>Bad gateway</html>');
            await response.close();
          },
        ]);

        await expectLater(
          app().health.get(),
          throwsA(
            isA<OrvanoException>()
                .having((e) => e.status, 'status', 502)
                .having((e) => e.code, 'code', 'unknown')
                .having((e) => e.requestId, 'requestId', 'header-id'),
          ),
        );
      },
    );

    test('exposes the generated error codes as constants', () {
      expect(
        (ErrorCode.notFound, ErrorCode.internalError),
        ('not_found', 'internal_error'),
      );
    });
  });

  group('paginate and events', () {
    test('paginate walks every page in order (AC-7)', () async {
      final pages = {
        null: (items: [1, 2], nextCursor: 'b'),
        'b': (items: [3, 4], nextCursor: 'c'),
        'c': (items: [5], nextCursor: null),
      };
      final cursors = <String?>[];

      final items =
          await paginate<({List<int> items, String? nextCursor}), int>((
            cursor,
          ) async {
            cursors.add(cursor);
            return pages[cursor]!;
          }, (page) => (page.items, page.nextCursor)).toList();

      expect(items, [1, 2, 3, 4, 5]);
      expect(cursors, [null, 'b', 'c']);
    });

    test(
      'decodeEvent returns null for an event this SDK does not know (AC-8)',
      () {
        expect(decodeEvent('nobody.knows', {'a': 1}), isNull);
      },
    );

    test('decodeEvent decodes through the registry it is given (AC-8)', () {
      final decoded = decodeEvent(
        'things.created',
        {'id': 't1'},
        registry: {'things.created': (json) => json['id'] as String},
      );

      expect(decoded, 't1');
    });
  });
}
