// Spec 0004 DQA-5: the shared runtime's required behavior (retry, timeout,
// cancel, close, the auth hook, error mapping), tested once here where the
// templates live (PKG-3). Hand written; the generator never touches test/core.
import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:fake_async/fake_async.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_server/src/core/cancel_token.dart';
import 'package:orvano_server/src/core/exceptions.dart';
import 'package:orvano_server/src/core/options.dart';
import 'package:orvano_server/src/core/transport.dart';
import 'package:test/test.dart';

OrvanoTransport _transport(
  http.Client client, {
  OrvanoClientOptions options = const OrvanoClientOptions(),
  AuthHeaderProvider? auth,
}) => OrvanoTransport(
  endpoint: Uri.parse('https://orvano.test/base'),
  projectId: 'p1',
  clientName: 'orvano_server/1.2.3',
  auth: auth ?? KeyAuth('orv_test_key'),
  options: options,
  httpClient: client,
  random: Random(1),
);

http.Response _json(Object body, int status, {Map<String, String>? headers}) =>
    http.Response(
      jsonEncode(body),
      status,
      headers: {'content-type': 'application/json', ...?headers},
    );

http.Response _problem(
  int status,
  String code, {
  Map<String, String>? headers,
}) => _json(
  {
    'type': 'about:blank',
    'title': 'Status $status',
    'status': status,
    'detail': 'A scripted $status.',
    'code': code,
    'traceId': '0af7651916cd43dd8448eb211c80319c',
  },
  status,
  headers: headers,
);

/// Runs [body] under fake time and returns what its future gave.
({Object? value, Object? error}) _run(
  Future<Object?> Function() body, {
  Duration elapse = const Duration(minutes: 5),
}) {
  ({Object? value, Object? error})? result;
  fakeAsync((async) {
    unawaited(
      body().then<void>(
        (value) => result = (value: value, error: null),
        onError: (Object error) => result = (value: null, error: error),
      ),
    );
    async.elapse(elapse);
  });
  return result ?? (value: null, error: StateError('still pending'));
}

void main() {
  group('OrvanoTransport', () {
    test(
      'sends the SDK headers and resolves the path under the endpoint',
      () async {
        late http.BaseRequest seen;
        final transport = _transport(
          MockClient((request) async {
            seen = request;
            return _json({'ok': true}, 200);
          }),
        );
        final result = await transport.request(
          operationId: 'Test',
          method: 'GET',
          path: ['v1', 'projects', transport.projectId, 'a b'],
          query: {
            'limit': 5,
            'tag': ['x', 'y'],
            'missing': null,
            'on': true,
          },
        );
        expect(result, {'ok': true});
        expect(
          seen.url.toString(),
          'https://orvano.test/base/v1/projects/p1/a%20b?limit=5&tag=x&tag=y&on=true',
        );
        expect(seen.headers['Orvano-Api-Version'], isNotEmpty);
        expect(seen.headers['Orvano-Client'], 'orvano_server/1.2.3');
        expect(seen.headers['Orvano-Key'], 'orv_test_key');
        expect(seen.followRedirects, isFalse);
      },
    );

    test('sends a JSON body as UTF 8', () async {
      late http.Request seen;
      final transport = _transport(
        MockClient((request) async {
          seen = request;
          return http.Response('', 204);
        }),
      );
      final result = await transport.request(
        operationId: 'Test',
        method: 'POST',
        path: ['v1', 'x'],
        body: {'name': 'Zoë'},
      );
      expect(result, isNull);
      expect(seen.headers['Content-Type'], startsWith('application/json'));
      expect(utf8.decode(seen.bodyBytes), '{"name":"Zoë"}');
    });

    test('retries a GET after a 503, with a fresh request each time', () {
      final requests = <http.BaseRequest>[];
      final transport = _transport(
        MockClient((request) async {
          requests.add(request);
          return requests.length == 1
              ? _problem(503, 'unavailable')
              : _json({'status': 'ok'}, 200);
        }),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
      );
      expect(result.value, {'status': 'ok'});
      expect(requests, hasLength(2));
      expect(identical(requests[0], requests[1]), isFalse);
    });

    test('does not retry a POST after a 503', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          return _problem(503, 'unavailable');
        }),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'POST', path: ['v1']),
      );
      expect(
        result.error,
        isA<OrvanoApiException>()
            .having((e) => e.statusCode, 'statusCode', 503)
            .having((e) => e.kind, 'kind', OrvanoErrorKind.server),
      );
      expect(calls, 1);
    });

    test('stops after maxRetries', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          return _problem(502, 'unavailable');
        }),
      );
      _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
      );
      expect(calls, 3);
    });

    test('waits Retry-After seconds before the retry', () {
      var calls = 0;
      fakeAsync((async) {
        final transport = _transport(
          MockClient((request) async {
            calls++;
            return calls == 1
                ? _problem(429, 'rate_limited', headers: {'retry-after': '2'})
                : _json({'status': 'ok'}, 200);
          }),
        );
        unawaited(
          transport.request(operationId: 'T', method: 'GET', path: ['v1']),
        );
        async.elapse(const Duration(milliseconds: 1999));
        expect(calls, 1);
        async.elapse(const Duration(milliseconds: 2));
        expect(calls, 2);
      });
    });

    test('throws when Retry-After is longer than maxRetryAfter', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          return _problem(429, 'rate_limited', headers: {'retry-after': '60'});
        }),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
      );
      expect(
        result.error,
        isA<OrvanoRateLimitedException>()
            .having(
              (e) => e.retryAfter,
              'retryAfter',
              const Duration(seconds: 60),
            )
            .having((e) => e.kind, 'kind', OrvanoErrorKind.rateLimited),
      );
      expect(calls, 1);
    });

    test(
      'turns a 3xx into a network error and never follows or retries it',
      () {
        var calls = 0;
        final transport = _transport(
          MockClient((request) async {
            calls++;
            return http.Response('', 302, headers: {'location': '/elsewhere'});
          }),
        );
        final result = _run(
          () =>
              transport.request(operationId: 'T', method: 'GET', path: ['v1']),
        );
        expect(result.error, isA<OrvanoNetworkException>());
        expect(calls, 1);
      },
    );

    test('does not retry a TLS failure', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          throw const HandshakeException('bad certificate');
        }),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
      );
      expect(result.error, isA<OrvanoNetworkException>());
      expect(calls, 1);
    });

    test('retries a failed connection, then gives OrvanoNetworkException', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          throw http.ClientException('connection refused');
        }),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
      );
      expect(
        result.error,
        isA<OrvanoNetworkException>().having(
          (e) => e.cause,
          'cause',
          isA<http.ClientException>(),
        ),
      );
      expect(calls, 3);
    });

    test('times out with a client that ignores aborting', () {
      final transport = _transport(
        MockClient((request) => Completer<http.Response>().future),
        options: const OrvanoClientOptions(
          timeout: Duration(seconds: 1),
          retry: RetryOptions(maxRetries: 0),
        ),
      );
      final result = _run(
        () => transport.request(operationId: 'T', method: 'GET', path: ['v1']),
        elapse: const Duration(seconds: 2),
      );
      expect(
        result.error,
        isA<OrvanoTimeoutException>().having(
          (e) => e.timeout,
          'timeout',
          const Duration(seconds: 1),
        ),
      );
    });

    test('a cancelled token fails before anything is sent', () {
      var calls = 0;
      final transport = _transport(
        MockClient((request) async {
          calls++;
          return _json({}, 200);
        }),
      );
      final token = OrvanoCancelToken()..cancel('gone');
      final result = _run(
        () => transport.request(
          operationId: 'T',
          method: 'GET',
          path: ['v1'],
          options: OrvanoRequestOptions(cancelToken: token),
        ),
      );
      expect(
        result.error,
        isA<OrvanoCancelledException>().having(
          (e) => e.reason,
          'reason',
          'gone',
        ),
      );
      expect(calls, 0);
    });

    test('cancelling mid flight releases the caller at once', () {
      fakeAsync((async) {
        final token = OrvanoCancelToken();
        final transport = _transport(
          MockClient((request) => Completer<http.Response>().future),
        );
        Object? error;
        unawaited(
          transport
              .request(
                operationId: 'T',
                method: 'GET',
                path: ['v1'],
                options: OrvanoRequestOptions(cancelToken: token),
              )
              .then<void>((_) {}, onError: (Object e) => error = e),
        );
        async.elapse(const Duration(milliseconds: 100));
        token.cancel('screen closed');
        async.flushMicrotasks();
        expect(
          error,
          isA<OrvanoCancelledException>()
              .having((e) => e.reason, 'reason', 'screen closed')
              .having((e) => e.kind, 'kind', OrvanoErrorKind.cancelled),
        );
      });
    });

    test('cancelling during a backoff wait ends the wait', () {
      fakeAsync((async) {
        final token = OrvanoCancelToken();
        var calls = 0;
        final transport = _transport(
          MockClient((request) async {
            calls++;
            return _problem(503, 'unavailable', headers: {'retry-after': '20'});
          }),
        );
        Object? error;
        unawaited(
          transport
              .request(
                operationId: 'T',
                method: 'GET',
                path: ['v1'],
                options: OrvanoRequestOptions(cancelToken: token),
              )
              .then<void>((_) {}, onError: (Object e) => error = e),
        );
        async.elapse(const Duration(seconds: 1));
        token.cancel();
        async.flushMicrotasks();
        expect(error, isA<OrvanoCancelledException>());
        expect(calls, 1);
      });
    });

    test('close() aborts calls in flight and refuses later calls', () {
      fakeAsync((async) {
        final transport = _transport(
          MockClient((request) => Completer<http.Response>().future),
        );
        Object? error;
        unawaited(
          transport
              .request(operationId: 'T', method: 'GET', path: ['v1'])
              .then<void>((_) {}, onError: (Object e) => error = e),
        );
        async.elapse(const Duration(milliseconds: 100));
        transport
          ..close()
          ..close();
        async.flushMicrotasks();
        expect(
          error,
          isA<OrvanoCancelledException>().having(
            (e) => e.reason,
            'reason',
            OrvanoCancelReason.clientClosed,
          ),
        );
        Object? later;
        unawaited(
          transport
              .request(operationId: 'T', method: 'GET', path: ['v1'])
              .then<void>((_) {}, onError: (Object e) => later = e),
        );
        async.flushMicrotasks();
        expect(later, isA<StateError>());
      });
    });

    test(
      'a body that is not a problem gives code unknown and keeps 4 KB',
      () async {
        final transport = _transport(
          MockClient(
            (request) async => http.Response(
              '<html>${'x' * 5000}</html>',
              500,
              reasonPhrase: 'Internal Server Error',
            ),
          ),
        );
        await expectLater(
          transport.request(operationId: 'T', method: 'POST', path: ['v1']),
          throwsA(
            isA<OrvanoApiException>()
                .having((e) => e.code, 'code', 'unknown')
                .having((e) => e.title, 'title', 'Internal Server Error')
                .having((e) => e.rawBody.length, 'rawBody length', 4096),
          ),
        );
      },
    );

    test('reads field errors and the trace ID from a problem body', () async {
      final transport = _transport(
        MockClient(
          (request) async => _json(
            {
              'type': 'about:blank',
              'title': 'Bad Request',
              'status': 400,
              'detail': 'The request is not valid.',
              'code': 'validation_failed',
              'traceId': 'from-body',
              'errors': [
                {
                  'field': 'name',
                  'location': 'body',
                  'code': 'too_short',
                  'message': 'Too short.',
                },
              ],
            },
            400,
            headers: {'orvano-request-id': 'from-header'},
          ),
        ),
      );
      await expectLater(
        transport.request(operationId: 'T', method: 'POST', path: ['v1']),
        throwsA(
          isA<OrvanoApiException>()
              .having((e) => e.kind, 'kind', OrvanoErrorKind.validation)
              .having((e) => e.traceId, 'traceId', 'from-header')
              .having((e) => e.detail, 'detail', 'The request is not valid.')
              .having((e) => e.fieldErrors, 'fieldErrors', [
                const OrvanoFieldError(
                  field: 'name',
                  location: 'body',
                  code: 'too_short',
                  message: 'Too short.',
                ),
              ]),
        ),
      );
    });

    test('a 2xx body that is not JSON gives OrvanoProtocolException', () async {
      final transport = _transport(
        MockClient((request) async => http.Response('not json', 200)),
      );
      await expectLater(
        transport.request(operationId: 'T', method: 'GET', path: ['v1']),
        throwsA(isA<OrvanoProtocolException>()),
      );
    });

    test(
      'refreshes once on session_expired and sends the request again',
      () async {
        final auth = _RefreshingAuth();
        var calls = 0;
        final transport = _transport(
          MockClient((request) async {
            calls++;
            return request.headers['Authorization'] == 'Bearer fresh'
                ? _json({'ok': true}, 200)
                : _problem(401, 'session_expired');
          }),
          auth: auth,
        );
        final result = await transport.request(
          operationId: 'T',
          method: 'POST',
          path: ['v1'],
        );
        expect(result, {'ok': true});
        expect(calls, 2);
        expect(auth.refreshes, 1);
      },
    );

    test('a second session_expired throws without another refresh', () async {
      final auth = _RefreshingAuth(succeeds: false);
      final transport = _transport(
        MockClient((request) async => _problem(401, 'session_expired')),
        auth: auth,
      );
      await expectLater(
        transport.request(operationId: 'T', method: 'GET', path: ['v1']),
        throwsA(
          isA<OrvanoApiException>().having(
            (e) => e.kind,
            'kind',
            OrvanoErrorKind.unauthenticated,
          ),
        ),
      );
      expect(auth.refreshes, 1);
    });

    test(
      'refuses headers the SDK sets, and lets a call set the API version',
      () async {
        final transport = _transport(
          MockClient((request) async => _json({}, 200)),
        );
        await expectLater(
          transport.request(
            operationId: 'T',
            method: 'GET',
            path: ['v1'],
            options: const OrvanoRequestOptions(
              headers: {'orvano-key': 'other'},
            ),
          ),
          throwsArgumentError,
        );
        await transport.request(
          operationId: 'T',
          method: 'GET',
          path: ['v1'],
          options: const OrvanoRequestOptions(
            headers: {'Orvano-Api-Version': '1999-01-01'},
          ),
        );
        expect(
          () => _transport(
            MockClient((request) async => _json({}, 200)),
            options: const OrvanoClientOptions(
              defaultHeaders: {'Orvano-Api-Version': '1999-01-01'},
            ),
          ),
          throwsArgumentError,
        );
      },
    );
  });

  group('checkEndpoint', () {
    test('adds a trailing slash to the base path', () {
      expect(
        checkEndpoint(
          Uri.parse('https://example.com/orvano'),
          allowInsecure: false,
        ).toString(),
        'https://example.com/orvano/',
      );
    });

    test('allows http only for local hosts unless told otherwise', () {
      for (final local in [
        'http://localhost:8080',
        'http://127.0.0.1',
        'http://[::1]',
        'http://api.localhost',
      ]) {
        expect(
          () => checkEndpoint(Uri.parse(local), allowInsecure: false),
          returnsNormally,
        );
      }
      expect(
        () => checkEndpoint(Uri.parse('http://10.0.2.2'), allowInsecure: false),
        throwsArgumentError,
      );
      expect(
        () => checkEndpoint(Uri.parse('http://10.0.2.2'), allowInsecure: true),
        returnsNormally,
      );
    });

    test('refuses a relative URL, a user, a query, or a fragment', () {
      for (final bad in [
        '/v1',
        'https://user@example.com',
        'https://example.com?x=1',
        'https://example.com#x',
      ]) {
        expect(
          () => checkEndpoint(Uri.parse(bad), allowInsecure: false),
          throwsArgumentError,
        );
      }
    });
  });

  test('KeyAuth refuses an empty key and never shows it', () {
    expect(() => KeyAuth(''), throwsArgumentError);
    expect(KeyAuth('orv_sk_secret').toString(), isNot(contains('secret')));
  });
}

final class _RefreshingAuth implements AuthHeaderProvider {
  _RefreshingAuth({this.succeeds = true});

  final bool succeeds;
  int refreshes = 0;
  String _token = 'stale';

  @override
  Future<Map<String, String>> headers() async => {
    'Authorization': 'Bearer $_token',
  };

  @override
  Future<bool> onUnauthorized(OrvanoApiException error) async {
    refreshes++;
    if (succeeds) _token = 'fresh';
    return succeeds;
  }
}
