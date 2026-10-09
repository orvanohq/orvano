import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

// Spec 0013: verifyMfa and confirmTotp answer a RaisedSession with no refresh
// token, so the client keeps the one in secure storage and replaces only the
// access token; enrollment and linking send the user's current password.

final signedIn = AuthSession(
  accessToken: 'level1',
  accessTokenExpiresAt: DateTime.now().toUtc().add(const Duration(minutes: 15)),
  refreshToken: 'orv_rt_kept',
  refreshTokenExpiresAt: DateTime.utc(2030, 1, 2, 3, 4, 5),
  sessionId: 's1',
);

Map<String, Object?> raised(String access) => {
  'accessToken': access,
  'accessTokenExpiresAt': DateTime.now()
      .toUtc()
      .add(const Duration(minutes: 15))
      .toIso8601String(),
  'sessionId': 's1',
};

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  late List<http.Request> sent;

  setUp(() {
    sent = [];
    FlutterSecureStorage.setMockInitialValues({
      'orvano.session.shop': jsonEncode(signedIn.toJson()),
    });
  });

  Client client(Object? Function(String path) answer) => createClient(
    endpoint: 'https://orvano.example.com',
    project: 'shop',
    onWarning: (_) {},
    httpClient: MockClient((request) async {
      sent.add(request);
      return http.Response(
        jsonEncode(answer(request.url.path)),
        200,
        headers: {'content-type': 'application/json'},
      );
    }),
  );

  test('verifyMfa and confirmTotp store the new access token and keep the '
      'refresh token in secure storage', () async {
    final orvano = client(
      (path) => path.endsWith('/totp/confirm')
          ? {
              'recoveryCodes': ['AAAAA-BBBBB'],
              'session': raised('level2b'),
            }
          : raised('level2a'),
    );
    final events = <AuthEvent>[];
    orvano.authStateChanges.listen((c) => events.add(c.event));

    await orvano.verifyMfa(const MfaAnswer.totp('123456'));
    var stored = await SecureSessionStore('shop').read();
    expect(stored?.accessToken, 'level2a');
    expect(stored?.refreshToken, 'orv_rt_kept');
    expect(stored?.refreshTokenExpiresAt, signedIn.refreshTokenExpiresAt);

    expect(await orvano.confirmTotp('123456'), ['AAAAA-BBBBB']);
    stored = await SecureSessionStore('shop').read();
    expect(stored?.accessToken, 'level2b');
    expect(stored?.refreshToken, 'orv_rt_kept');
    await Future<void>.delayed(Duration.zero);

    expect(sent.map((r) => r.url.path), [
      '/v1/account/mfa/verify',
      '/v1/account/mfa/totp/confirm',
    ]);
    expect(events, [AuthEvent.tokenRefreshed, AuthEvent.tokenRefreshed]);
    orvano.close();
  });

  test('createTotp and linkIdentityWithIdToken send the password', () async {
    final orvano = client(
      (path) => path.endsWith('/mfa/totp')
          ? {
              'secret': 'S',
              'uri': 'otpauth://totp/x',
              'expiresAt': '2026-10-07T12:05:00Z',
            }
          : {
              'id': 'i1',
              'provider': 'google',
              'subject': 'g1',
              'email': null,
              'emailVerified': false,
              'createdAt': '2026-01-01T00:00:00Z',
              'lastSignInAt': null,
            },
    );

    await Orvano(
      orvano,
    ).account.createTotp(const CreateTotpRequest(password: 'correct horse'));
    await orvano.linkIdentityWithIdToken(
      provider: IdTokenProvider.google,
      idToken: 'id.token.value',
      nonce: OrvanoNonce.create().raw,
      password: 'correct horse',
    );

    expect(sent.map((r) => r.url.path), [
      '/v1/account/mfa/totp',
      '/v1/account/identities/id-token',
    ]);
    for (final request in sent) {
      expect(
        (jsonDecode(request.body) as Map<String, Object?>)['password'],
        'correct horse',
      );
    }
    orvano.close();
  });
}
