import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:orvano_core/orvano_core.dart';

import 'platform_passkeys.dart';
import 'secure_session_store.dart';
import 'web_auth_launcher.dart';

/// Creates a client for a Flutter app (spec 0004, AC-25): the session lives
/// in secure storage under `orvano.session.<projectId>`, and each time the
/// app resumes the client checks it, refreshing when under a minute of the
/// access token is left. Nothing refreshes on a timer. Provider sign in
/// (`signInWithOAuth`, `linkIdentity`) opens the system's auth session
/// through [webAuthLauncher] unless [oauthLauncher] says otherwise
/// (spec 0012), and passkeys (`signInWithPasskey`, `registerPasskey`, and
/// `MfaAnswer.passkey`) use [PlatformPasskeys] unless [passkeys] says
/// otherwise (spec 0013). Call it after
/// `WidgetsFlutterBinding.ensureInitialized()`; the resume check lasts as long
/// as the app.
///
/// ```dart
/// final orvano = Orvano(
///   createClient(endpoint: 'https://orvano.example.com', project: 'shop'),
/// );
/// ```
Client createClient({
  required String endpoint,
  required String project,
  SessionStore? session,
  FlutterSecureStorage? storage,
  Map<String, String> headers = const {},
  Duration timeout = const Duration(seconds: 30),
  int maxRetries = 3,
  http.Client? httpClient,
  void Function(String message)? onWarning,
  OAuthLauncher? oauthLauncher,
  PasskeyAuthenticator? passkeys,
}) {
  final client = Client(
    endpoint: endpoint,
    project: project,
    headers: headers,
    session: session ?? SecureSessionStore(project, storage: storage),
    timeout: timeout,
    maxRetries: maxRetries,
    httpClient: httpClient,
    onWarning: onWarning,
  );
  setDefaultOAuthLauncher(client, oauthLauncher ?? webAuthLauncher);
  setDefaultPasskeyAuthenticator(client, passkeys ?? PlatformPasskeys());
  watchResume(client);
  return client;
}

/// Checks [client]'s session each time the app resumes, refreshing it when
/// under a minute of the access token is left; a network error keeps the
/// session for the next call. Returns the listener, so you can dispose it.
AppLifecycleListener watchResume(Client client) =>
    AppLifecycleListener(onResume: () => unawaited(checkSession(client)));

/// The resume check itself: [Client.getSession], whose errors never reach
/// the app (the next call tries again).
Future<void> checkSession(Client client) async {
  try {
    await client.getSession();
  } on Object {
    // Offline or a timeout: the session stays, and the next call retries.
  }
}
