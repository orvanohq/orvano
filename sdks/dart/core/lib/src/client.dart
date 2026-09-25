import 'dart:convert';

import 'package:http/http.dart' as http;

import 'orvano_exception.dart';

/// Sends requests to one Orvano server. Generated services (`orvano.health`,
/// ...) all send through a [Client].
final class Client {
  /// Creates a client for the server at [endpoint], for example
  /// `https://orvano.example.com`. Pass [httpClient] to reuse or mock the
  /// underlying HTTP client.
  Client({
    required String endpoint,
    this.project,
    Map<String, String> headers = const {},
    http.Client? httpClient,
  }) : endpoint = _parseEndpoint(endpoint),
       _headers = Map.unmodifiable(headers),
       _http = httpClient ?? http.Client();

  /// The server's base URL, without a trailing slash.
  final String endpoint;

  /// The project ID, sent as `X-Orvano-Project` on every call.
  final String? project;

  final Map<String, String> _headers;
  final http.Client _http;

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

  /// Sends one request and returns the decoded JSON body, or null when the
  /// response has none. Throws [OrvanoException] on a failure status.
  Future<Object?> send(
    String method,
    String path, {
    Map<String, String?> query = const {},
    Object? body,
    bool idempotent = false,
  }) async {
    final params = {for (final e in query.entries) e.key: ?e.value};
    var uri = Uri.parse('$endpoint$path');
    if (params.isNotEmpty) uri = uri.replace(queryParameters: params);

    final request = http.Request(method, uri)
      ..headers.addAll(_headers)
      ..headers['Accept'] = 'application/json';
    if (project case final project?) {
      request.headers['X-Orvano-Project'] = project;
    }
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }

    final response = await http.Response.fromStream(await _http.send(request));
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw OrvanoException.fromResponse(
        response.statusCode,
        response.body,
        response.headers['x-request-id'],
      );
    }
    if (response.statusCode == 204 ||
        method == 'HEAD' ||
        response.body.isEmpty) {
      return null;
    }
    return jsonDecode(response.body);
  }

  /// Closes the underlying HTTP client. The client can't be used afterwards.
  void close() => _http.close();
}
