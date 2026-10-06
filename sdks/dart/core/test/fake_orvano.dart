import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// How the fake answers one request.
typedef Answer = Future<void> Function(HttpResponse response);

/// A local HTTP server standing in for Orvano: it answers from a list (the
/// last answer repeats) and records the headers, path, method, and body of
/// every request.
final class FakeOrvano {
  FakeOrvano._(this._server, this._answers) {
    _server.listen((request) async {
      requests.add(request.headers);
      paths.add(request.uri.path);
      methods.add(request.method);
      bodies.add(await utf8.decodeStream(request));
      final answer =
          _answers[_next < _answers.length ? _next++ : _answers.length - 1];
      await answer(request.response);
    });
  }

  /// Starts a server on a free local port.
  static Future<FakeOrvano> start(List<Answer> answers) async => FakeOrvano._(
    await HttpServer.bind(InternetAddress.loopbackIPv4, 0),
    answers,
  );

  final HttpServer _server;
  final List<Answer> _answers;
  var _next = 0;

  /// The headers of every request, in order.
  final requests = <HttpHeaders>[];

  /// The path of every request, in order.
  final paths = <String>[];

  /// The method of every request, in order.
  final methods = <String>[];

  /// The body of every request, in order (empty when it had none).
  final bodies = <String>[];

  /// The JSON body of request [index].
  Map<String, Object?> jsonBody(int index) =>
      jsonDecode(bodies[index]) as Map<String, Object?>;

  /// The base URL to give the client.
  String get endpoint => 'http://127.0.0.1:${_server.port}';

  /// Stops the server.
  Future<void> close() => _server.close(force: true);
}

/// A health answer from a server running [version].
Answer health([String version = '0.0.0']) => (response) async {
  response.headers
    ..contentType = ContentType.json
    ..set('X-Orvano-Version', version);
  response.write(jsonEncode({'status': 'ok', 'version': version}));
  await response.close();
};

/// A bare [code], optionally with `Retry-After`.
Answer status(int code, {String? retryAfter}) => (response) async {
  response.statusCode = code;
  if (retryAfter != null) response.headers.set('Retry-After', retryAfter);
  await response.close();
};

/// A problem details answer, optionally with `X-Request-Id` and `Retry-After`.
Answer problem(
  int code,
  Map<String, Object?> body, {
  String? requestId,
  String? retryAfter,
}) => (response) async {
  response
    ..statusCode = code
    ..headers.set('Content-Type', 'application/problem+json');
  if (requestId != null) response.headers.set('X-Request-Id', requestId);
  if (retryAfter != null) response.headers.set('Retry-After', retryAfter);
  response.write(jsonEncode(body));
  await response.close();
};

/// Never answers, until the client gives up.
Answer hang() =>
    (response) => Completer<void>().future;
