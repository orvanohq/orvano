import 'dart:convert';
import 'dart:io';

import 'package:dart_quickstart/orvano.dart';
import 'package:orvano_dart/orvano_dart.dart';
import 'package:shelf/shelf.dart';
import 'package:shelf/shelf_io.dart' as shelf_io;

Future<void> main() async {
  // Dart creates `client` the first time it's used. Using it here reads the
  // settings now, so a missing one stops the server before it listens.
  print('Orvano project: ${client.project}');
  final server = await shelf_io.serve(
    _route,
    InternetAddress.loopbackIPv4,
    3001,
  );
  print('Listening on http://localhost:${server.port}');
}

Future<Response> _route(Request request) async {
  if (request.method == 'GET' && request.url.path == 'me') return me(request);
  return Response.notFound('Not found');
}

// #region me
/// `GET /me`: checks the access token in `Authorization: Bearer <token>`, then
/// answers with the user it belongs to. A missing or bad token gets 401.
Future<Response> me(Request request) async {
  final header = request.headers['authorization'] ?? '';
  if (!header.startsWith('Bearer ')) return _unauthorized();

  try {
    // Checks the signature, the project, and the expiry, without a call to
    // Orvano once the project's public keys are cached.
    final token = await client.verifyAccessToken(
      header.substring('Bearer '.length),
    );
    final user = await orvano.users.get(token.userId);
    return Response.ok(
      jsonEncode({'id': user.id, 'email': user.email, 'name': user.name}),
      headers: {'content-type': 'application/json'},
    );
  } on OrvanoException catch (error) {
    // invalid_token or token_expired; anything else is a real failure.
    if (error.status != 401) rethrow;
    return _unauthorized();
  }
}

Response _unauthorized() => Response.unauthorized(
  jsonEncode({'error': 'Send a valid access token.'}),
  headers: {'content-type': 'application/json', 'www-authenticate': 'Bearer'},
);
// #endregion me
