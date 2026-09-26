@TestOn('vm')
library;

import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_dart/orvano_dart.dart';
import 'package:test/test.dart';

/// A client whose requests land in [sent], each answered with a health body.
Client client(List<http.BaseRequest> sent, {String? apiKey}) => Client(
  endpoint: 'https://orvano.example.com',
  apiKey: apiKey,
  onWarning: (_) {},
  httpClient: MockClient((request) async {
    sent.add(request);
    return http.Response(
      '{"status":"ok","version":"0.0.0"}',
      200,
      headers: {'content-type': 'application/json'},
    );
  }),
);

void main() {
  test('sends the API key on every call (AC-4)', () async {
    final sent = <http.BaseRequest>[];

    await Orvano(client(sent, apiKey: 'test-server-key')).health.get();

    expect(sent.single.headers['X-Orvano-Key'], 'test-server-key');
  });

  test('stops sending the key once it is removed (AC-4)', () async {
    final sent = <http.BaseRequest>[];
    final c = client(sent, apiKey: 'test-server-key');

    c.apiKey = null;
    await Orvano(c).health.get();

    expect(sent.single.headers.containsKey('X-Orvano-Key'), isFalse);
  });

  test('names itself orvano_dart in the SDK header (AC-11)', () async {
    final sent = <http.BaseRequest>[];

    await Orvano(client(sent)).health.get();

    expect(sent.single.headers['X-Orvano-SDK'], 'orvano_dart/$sdkVersion');
  });
}
