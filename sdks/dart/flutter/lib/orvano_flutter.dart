/// Orvano for Flutter apps. It re-exports `orvano_core`, which carries only
/// `client` and `both` operations, so no API key can end up in an app. The
/// session lives in secure storage and is checked when the app resumes
/// (spec 0004, AC-25).
///
/// ```dart
/// WidgetsFlutterBinding.ensureInitialized();
/// final orvano = Orvano(
///   createClient(endpoint: 'https://orvano.example.com', project: 'shop'),
/// );
/// orvano.client.authStateChanges.listen((change) => print(change.event));
/// ```
library;

export 'package:orvano_core/orvano_core.dart';

export 'src/flutter_client.dart' show checkSession, createClient, watchResume;
export 'src/secure_session_store.dart' show SecureSessionStore;
export 'src/web_auth_launcher.dart' show webAuthLauncher;
