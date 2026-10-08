import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

// Spec 0013 AC-38: orvano_flutter has no passkey dependency and sets no
// default authenticator. Native passkeys come from the opt in
// orvano_flutter_passkeys package, passed to createClient.

final class _Refused implements Exception {}

final class _FakePasskeys implements PasskeyAuthenticator {
  PasskeyRequestOptions? asked;

  @override
  Future<PasskeyRegistrationCredential> create(
    PasskeyCreationOptions options,
  ) => throw _Refused();

  @override
  Future<PasskeyAssertionCredential> get(
    PasskeyRequestOptions options, {
    bool autofill = false,
  }) {
    asked = options;
    throw _Refused();
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() => FlutterSecureStorage.setMockInitialValues({}));

  late List<String> paths;

  Client client({PasskeyAuthenticator? passkeys}) => createClient(
    endpoint: 'https://orvano.example.com',
    project: 'shop',
    onWarning: (_) {},
    passkeys: passkeys,
    httpClient: MockClient((request) async {
      paths.add(request.url.path);
      return http.Response(
        jsonEncode({
          'challengeId': 'pkc_1',
          'options': {
            'challenge': 'Y2hhbGxlbmdl',
            'rpId': 'example.com',
            'timeout': 300000,
            'userVerification': 'required',
            'allowCredentials': <Object>[],
          },
        }),
        200,
        headers: {'content-type': 'application/json'},
      );
    }),
  );

  setUp(() => paths = []);

  test(
    'with no authenticator, the passkey helpers name orvano_flutter_passkeys '
    'before any call',
    () async {
      final orvano = client();

      for (final call in <Future<Object?> Function()>[
        orvano.signInWithPasskey,
        orvano.registerPasskey,
      ]) {
        await expectLater(
          call(),
          throwsA(
            isA<ArgumentError>().having(
              (e) => e.message,
              'message',
              allOf(
                contains('orvano_flutter_passkeys'),
                contains('PlatformPasskeys()'),
              ),
            ),
          ),
        );
      }
      expect(paths, isEmpty);
      orvano.close();
    },
  );

  test('uses the authenticator passed to createClient', () async {
    final passkeys = _FakePasskeys();
    final orvano = client(passkeys: passkeys);

    await expectLater(orvano.signInWithPasskey(), throwsA(isA<_Refused>()));

    expect(paths, ['/v1/account/sessions/passkey-challenge']);
    expect(passkeys.asked?.rpId, 'example.com');
    orvano.close();
  });
}
