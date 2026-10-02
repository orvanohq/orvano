import 'dart:convert';

import 'package:http_parser/http_parser.dart' show parseHttpDate;

/// The one exception every Orvano call throws when the server answers with a
/// failure. The server sends RFC 9457 problem details; [code] is Orvano's
/// stable error code (for example `user_already_exists`).
final class OrvanoException implements Exception {
  /// Creates an [OrvanoException].
  const OrvanoException({
    required this.status,
    required this.code,
    required this.message,
    this.requestId,
    this.retryAfter,
  });

  /// Reads a failed response body into an [OrvanoException], whatever it
  /// looks like.
  factory OrvanoException.fromResponse(
    int status,
    String body,
    String? requestIdHeader, {
    String? retryAfterHeader,
  }) {
    Map<String, dynamic> problem = const {};
    try {
      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) problem = decoded;
    } on FormatException {
      // Not JSON (a proxy error page, for example); fall back to the status.
    }

    String? text(String key) {
      final value = problem[key];
      return value is String && value.isNotEmpty ? value : null;
    }

    return OrvanoException(
      status: status,
      code: text('code') ?? 'unknown',
      message:
          text('detail') ??
          text('title') ??
          'Request failed with status $status',
      requestId: text('requestId') ?? requestIdHeader,
      retryAfter: parseRetryAfter(retryAfterHeader),
    );
  }

  /// `Retry-After` as a [Duration]: a number of seconds, or an HTTP date;
  /// null when absent or unreadable.
  static Duration? parseRetryAfter(String? header, {DateTime? now}) {
    if (header == null || header.trim().isEmpty) return null;
    final seconds = int.tryParse(header.trim());
    if (seconds != null && seconds >= 0) return Duration(seconds: seconds);
    try {
      final wait = parseHttpDate(header).difference(now ?? DateTime.now());
      return wait.isNegative ? Duration.zero : wait;
    } on FormatException {
      return null;
    }
  }

  /// The HTTP status code.
  final int status;

  /// Orvano's stable error code, or `unknown` when the response carried none.
  final String code;

  /// A human readable description of the problem.
  final String message;

  /// The request ID to quote when reporting a problem, when the server sent
  /// one.
  final String? requestId;

  /// How long to wait before trying again, from the `Retry-After` header (a
  /// 429 or 503); null when the server sent none.
  final Duration? retryAfter;

  @override
  String toString() => 'OrvanoException($status, $code): $message';
}
