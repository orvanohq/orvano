import 'dart:convert';

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
  });

  /// Reads a failed response body into an [OrvanoException], whatever it
  /// looks like.
  factory OrvanoException.fromResponse(
    int status,
    String body,
    String? requestIdHeader,
  ) {
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
    );
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

  @override
  String toString() => 'OrvanoException($status, $code): $message';
}
