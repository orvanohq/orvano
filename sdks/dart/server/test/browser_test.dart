@TestOn('browser')
library;

import 'package:orvano_dart/orvano_dart.dart';
import 'package:test/test.dart';

void main() {
  test('refuses an API key in a browser (AC-4)', () {
    expect(
      () => Client(endpoint: 'https://orvano.example.com', apiKey: 'k'),
      throwsA(isA<UnsupportedError>()),
    );
  });

  test('allows a client without a key in a browser', () {
    expect(
      () => Client(endpoint: 'https://orvano.example.com'),
      returnsNormally,
    );
  });
}
