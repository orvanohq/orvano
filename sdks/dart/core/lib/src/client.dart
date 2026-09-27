import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart' show parseHttpDate;

import 'auth.dart';
import 'generated/version.dart';
import 'orvano_exception.dart';
import 'version.dart';

/// What a successful call does to the client's stored session, from the
/// contract's `x-orvano-session`.
enum SessionChange {
  /// A sign in: the response's `session` becomes the stored session.
  start,

  /// A refresh: the response itself is the new session.
  refresh,

  /// A sign out: the stored session is cleared.
  end,

  /// The signed in user changed: the session stays, and listeners hear
  /// [AuthEvent.userUpdated].
  user,
}

/// How a client trades a session for a fresh one. Throws an
/// [OrvanoException] with status 401 when the session is over; any other
/// error (a network error, a timeout) keeps the session.
typedef SessionRefresher =
    Future<AuthSession> Function(AuthSession session, Client client);

/// A client refreshes before a call when less than this is left of the
/// access token (spec 0004, AC-26).
const refreshMargin = Duration(seconds: 60);

/// The default [SessionRefresher]: `account.refreshSession` with the stored
/// refresh token.
Future<AuthSession> refreshWithToken(
  AuthSession session,
  Client client,
) async => AuthSession.fromJson(
  await client.send(
    'POST',
    '/v1/account/sessions/refresh',
    body: {'refreshToken': session.refreshToken},
    idempotent: true,
    anonymous: true,
  ),
);

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
/// gives every call a timeout. When someone is signed in it sends their
/// access token, refreshes it before a call when under a minute is left, and
/// after a 401 for an expired or refused token refreshes once and repeats the
/// call once (spec 0004, AC-26). Nothing refreshes on a timer.
base class Client {
  /// Creates a client for the server at [endpoint], for example
  /// `https://orvano.example.com`. [session] holds the signed in user's
  /// token (memory by default). [timeout] bounds one call, retries included;
  /// [maxRetries] caps the retries of a safe call. Pass [httpClient] to reuse
  /// or mock the underlying HTTP client. [onWarning] receives warnings, for
  /// example the one sent when the server's major.minor differs from this
  /// SDK's; it prints them by default.
  Client({
    required String endpoint,
    this.project,
    Map<String, String> headers = const {},
    SessionStore? session,
    this.timeout = const Duration(seconds: 30),
    this.maxRetries = 3,
    http.Client? httpClient,
    void Function(String message)? onWarning,
    SessionRefresher? refresh,
  }) : endpoint = _parseEndpoint(endpoint),
       session = session ?? MemorySessionStore(),
       _refresher = refresh ?? refreshWithToken,
       _headers = Map.unmodifiable(headers),
       _http = httpClient ?? http.Client(),
       _onWarning = onWarning ?? print;

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
  final void Function(String message) _onWarning;
  final Random _random = Random();
  final SessionRefresher _refresher;
  final _changes = StreamController<AuthStateChange>.broadcast();
  Future<AuthSession?>? _refreshing;
  bool _versionChecked = false;

  /// Every change to the signed in user: [AuthEvent.signedIn],
  /// [AuthEvent.signedOut], [AuthEvent.tokenRefreshed], and
  /// [AuthEvent.userUpdated].
  Stream<AuthStateChange> get authStateChanges => _changes.stream;

  /// The signed in user's session, refreshed first when under a minute of
  /// its access token is left; null when nobody is signed in. Call it when
  /// the app wakes up (`orvano_flutter` does, on resume).
  Future<AuthSession?> getSession() => _sessionForCall();

  /// The SDK's name, sent with its version in `X-Orvano-SDK`. Packages that
  /// build on this client name themselves here.
  String get sdkName => 'orvano_core';

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

  /// Adds this client's credentials to an outgoing request: the signed in
  /// user's access token as `Authorization: Bearer`, when there is one
  /// ([current]). Clients for other audiences add theirs here too.
  Future<void> authorize(
    Map<String, String> headers,
    AuthSession? current,
  ) async {
    if (current != null) {
      headers[authorizationHeader] = 'Bearer ${current.accessToken}';
    }
  }

  /// Sends one request and returns the decoded JSON body, or null when the
  /// response has none. Throws [OrvanoException] on a failure status and
  /// [TimeoutException] when the call takes longer than its timeout.
  ///
  /// [bearer] sends that access token as `Authorization: Bearer` instead of
  /// this client's own credentials, so a call made as a user never carries an
  /// API key. [noCache] sends `Cache-Control: no-cache`. [anonymous] sends no
  /// user credentials and never refreshes first (the refresh call itself).
  Future<Object?> send(
    String method,
    String path, {
    Map<String, String?> query = const {},
    Object? body,
    bool idempotent = false,
    SessionChange? session,
    RequestOptions? options,
    String? bearer,
    bool noCache = false,
    bool anonymous = false,
  }) async {
    final asUser =
        !anonymous &&
        bearer == null &&
        session != SessionChange.start &&
        session != SessionChange.refresh;
    final current = asUser ? await _sessionForCall() : null;
    Future<Object?> attempt(AuthSession? user) => _send(
      method,
      path,
      query: query,
      body: body,
      idempotent: idempotent,
      change: session,
      options: options,
      bearer: bearer,
      noCache: noCache,
      user: user,
    );
    try {
      return await attempt(current);
    } on OrvanoException catch (e) {
      // Refused before it did anything, so repeating any method once is safe.
      if (current == null || !_isStaleToken(e)) rethrow;
      final fresh = await _refresh(current);
      if (fresh == null) rethrow;
      return attempt(fresh);
    }
  }

  static bool _isStaleToken(OrvanoException e) =>
      e.status == 401 &&
      (e.code == 'token_expired' || e.code == 'invalid_token');

  /// The stored session, refreshed first when under [refreshMargin] is left.
  Future<AuthSession?> _sessionForCall() async {
    final current = await session.read();
    if (current == null ||
        current.accessTokenExpiresAt.difference(DateTime.now()) >=
            refreshMargin) {
      return current;
    }
    try {
      return await _refresh(current);
    } on Object {
      // A network error or timeout keeps the session; the call goes ahead
      // with the current token.
      return current;
    }
  }

  /// Trades [stale] for a fresh session, once per client at a time. Returns
  /// null when the session is over (a 401: the store is cleared and
  /// listeners hear [AuthEvent.signedOut]); rethrows anything else, keeping
  /// the session.
  Future<AuthSession?> _refresh(AuthSession stale) =>
      _refreshing ??= _refreshNow(stale).whenComplete(() => _refreshing = null);

  Future<AuthSession?> _refreshNow(AuthSession stale) async {
    // A call racing this one may have refreshed already.
    final current = await session.read();
    if (current == null) return null;
    if (current.accessToken != stale.accessToken) return current;
    try {
      final fresh = await _refresher(current, this);
      await _save(fresh, AuthEvent.tokenRefreshed);
      return fresh;
    } on OrvanoException catch (e) {
      if (e.status != 401) rethrow;
      await _save(null, AuthEvent.signedOut);
      return null;
    }
  }

  Future<void> _save(AuthSession? next, AuthEvent event) async {
    await session.write(next);
    if (!_changes.isClosed) _changes.add(AuthStateChange(event, next));
  }

  Future<Object?> _send(
    String method,
    String path, {
    required Map<String, String?> query,
    required Object? body,
    required bool idempotent,
    required SessionChange? change,
    required RequestOptions? options,
    required String? bearer,
    required bool noCache,
    required AuthSession? user,
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
          sdkHeader: '$sdkName/$sdkVersion',
          'X-Orvano-Project': ?project,
        };
        if (bearer != null) {
          headers[authorizationHeader] = 'Bearer $bearer';
        } else {
          await authorize(headers, user);
        }
        if (noCache) headers['Cache-Control'] = 'no-cache';
        final request = http.AbortableRequest(
          method,
          uri,
          abortTrigger: deadline?.future,
        )..headers.addAll(headers);
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

        _checkVersion(response.headers[serverVersionHeader.toLowerCase()]);
        final status = response.statusCode;
        if (status >= 200 && status < 300) {
          final Object? result =
              status == 204 || method == 'HEAD' || response.body.isEmpty
              ? null
              : jsonDecode(response.body);
          await _applySession(change, result);
          return result;
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

  /// Stores or clears the session after a successful sign in, refresh, or
  /// sign out.
  Future<void> _applySession(SessionChange? change, Object? result) async {
    switch (change) {
      case null:
        return;
      case SessionChange.start:
        final body = result is Map<String, dynamic> ? result : null;
        await _save(AuthSession.fromJson(body?['session']), AuthEvent.signedIn);
      case SessionChange.refresh:
        await _save(AuthSession.fromJson(result), AuthEvent.tokenRefreshed);
      case SessionChange.end:
        await _save(null, AuthEvent.signedOut);
      case SessionChange.user:
        if (!_changes.isClosed) {
          _changes.add(
            AuthStateChange(AuthEvent.userUpdated, await session.read()),
          );
        }
    }
  }

  /// Warns once per client when the server's major.minor differs from this
  /// SDK's.
  void _checkVersion(String? serverVersion) {
    if (_versionChecked || serverVersion == null) return;
    _versionChecked = true;
    final warning = versionMismatch(sdkName, serverVersion, endpoint);
    if (warning != null) _onWarning(warning);
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

  /// Closes the underlying HTTP client and [authStateChanges]. The client
  /// can't be used afterwards.
  void close() {
    _http.close();
    unawaited(_changes.close());
  }
}
