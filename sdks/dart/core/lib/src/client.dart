import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart' show parseHttpDate;

import 'auth.dart';
import 'orvano_exception.dart';

/// Per call settings, the last argument of every generated method.
final class RequestOptions {
  /// Creates options for one call.
  const RequestOptions({this.timeout});

  /// Overrides the client's timeout for this call; [Duration.zero] turns it
  /// off.
  final Duration? timeout;
}

/// Sends requests to one Orvano server. Generated services (`orvano.health`,
/// ...) all send through a [Client]. It retries safe calls (GET, HEAD, and
/// operations marked idempotent) on 429 and 503, honoring `Retry-After`, and
/// gives every call a timeout.
base class Client {
  /// Creates a client for the server at [endpoint], for example
  /// `https://orvano.example.com`. [session] holds the signed in user's
  /// token (memory by default). [timeout] bounds one call, retries included;
  /// [maxRetries] caps the retries of a safe call. Pass [httpClient] to reuse
  /// or mock the underlying HTTP client.
  Client({
    required String endpoint,
    this.project,
    Map<String, String> headers = const {},
    SessionStore? session,
    this.timeout = const Duration(seconds: 30),
    this.maxRetries = 3,
    http.Client? httpClient,
  }) : endpoint = _parseEndpoint(endpoint),
       session = session ?? MemorySessionStore(),
       _headers = Map.unmodifiable(headers),
       _http = httpClient ?? http.Client();

  /// The server's base URL, without a trailing slash.
  final String endpoint;

  /// The project ID, sent as `X-Orvano-Project` on every call.
  final String? project;

  /// Where the signed in user's session lives.
  final SessionStore session;

  /// How long one call may take, retries included; [Duration.zero] turns it
  /// off.
  final Duration timeout;

  /// How many times a safe call is retried after a 429 or 503.
  final int maxRetries;

  final Map<String, String> _headers;
  final http.Client _http;
  final Random _random = Random();

  static const _backoffBase = Duration(milliseconds: 250);

  static String _parseEndpoint(String endpoint) {
    final uri = Uri.tryParse(endpoint);
    if (uri == null || !uri.hasScheme || uri.host.isEmpty) {
      throw ArgumentError.value(
        endpoint,
        'endpoint',
        'must be an absolute URL',
      );
    }
    return endpoint.replaceFirst(RegExp(r'/+$'), '');
  }

  /// Adds this client's credentials to an outgoing request: the session
  /// token, when there is one. Clients for other audiences add theirs here
  /// too.
  Future<void> authorize(Map<String, String> headers) async {
    final token = await session.read();
    if (token != null && token.isNotEmpty) headers[sessionHeader] = token;
  }

  /// Sends one request and returns the decoded JSON body, or null when the
  /// response has none. Throws [OrvanoException] on a failure status and
  /// [TimeoutException] when the call takes longer than its timeout.
  Future<Object?> send(
    String method,
    String path, {
    Map<String, String?> query = const {},
    Object? body,
    bool idempotent = false,
    RequestOptions? options,
  }) async {
    final params = {for (final e in query.entries) e.key: ?e.value};
    var uri = Uri.parse('$endpoint$path');
    if (params.isNotEmpty) uri = uri.replace(queryParameters: params);
    final encoded = body == null ? null : jsonEncode(body);
    final retryable = method == 'GET' || method == 'HEAD' || idempotent;

    final limit = options?.timeout ?? timeout;
    final deadline = limit > Duration.zero ? Completer<void>() : null;
    final timer = deadline == null ? null : Timer(limit, deadline.complete);
    try {
      for (var attempt = 0; ; attempt++) {
        final headers = <String, String>{
          ..._headers,
          'Accept': 'application/json',
          'X-Orvano-Project': ?project,
        };
        await authorize(headers);
        final request =
            http.AbortableRequest(method, uri, abortTrigger: deadline?.future)
              ..headers.addAll(headers);
        if (encoded != null) {
          request.headers['Content-Type'] = 'application/json';
          request.body = encoded;
        }

        final http.Response response;
        try {
          response = await http.Response.fromStream(await _http.send(request));
        } on http.RequestAbortedException {
          throw TimeoutException('Orvano $method $path timed out', limit);
        }

        final status = response.statusCode;
        if (status >= 200 && status < 300) {
          if (status == 204 || method == 'HEAD' || response.body.isEmpty) {
            return null;
          }
          return jsonDecode(response.body);
        }

        if (retryable &&
            attempt < maxRetries &&
            (status == 429 || status == 503)) {
          final wait = _retryDelay(response.headers['retry-after'], attempt);
          if (!await _sleep(wait, deadline?.future)) {
            throw TimeoutException('Orvano $method $path timed out', limit);
          }
          continue;
        }
        throw OrvanoException.fromResponse(
          status,
          response.body,
          response.headers['x-request-id'],
        );
      }
    } finally {
      timer?.cancel();
    }
  }

  /// Waits [wait], or less when [deadline] arrives first; false then. The
  /// timer is cancelled either way, so nothing keeps the program alive.
  static Future<bool> _sleep(Duration wait, Future<void>? deadline) {
    final done = Completer<bool>();
    final timer = Timer(wait, () {
      if (!done.isCompleted) done.complete(true);
    });
    deadline?.then((_) {
      timer.cancel();
      if (!done.isCompleted) done.complete(false);
    });
    return done.future;
  }

  /// `Retry-After` (seconds or an HTTP date), else exponential backoff with
  /// full jitter.
  Duration _retryDelay(String? retryAfter, int attempt) {
    if (retryAfter != null) {
      final seconds = int.tryParse(retryAfter.trim());
      if (seconds != null && seconds >= 0) return Duration(seconds: seconds);
      try {
        final wait = parseHttpDate(retryAfter).difference(DateTime.now());
        return wait.isNegative ? Duration.zero : wait;
      } on FormatException {
        // Neither form; fall back to backoff.
      }
    }
    final ceiling = _backoffBase.inMicroseconds * pow(2, attempt);
    return Duration(microseconds: (_random.nextDouble() * ceiling).round());
  }

  /// Closes the underlying HTTP client. The client can't be used afterwards.
  void close() => _http.close();
}
