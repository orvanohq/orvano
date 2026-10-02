import 'package:flutter/material.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

import 'config.dart';
import 'home.dart';

// #region start
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  if (project.isEmpty) {
    throw StateError(
      'Run with --dart-define=ORVANO_PROJECT=<your project ID>.',
    );
  }

  // One client for the whole app. It keeps the session in the platform's
  // secure storage, and refreshes it when the app comes back to the front.
  final orvano = Orvano(createClient(endpoint: endpoint, project: project));
  runApp(QuickstartApp(orvano: orvano));
}
// #endregion start

/// The quickstart app.
class QuickstartApp extends StatelessWidget {
  /// Creates the app around one Orvano client.
  const QuickstartApp({super.key, required this.orvano});

  /// The Orvano client every screen uses.
  final Orvano orvano;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'Orvano Flutter quickstart',
    theme: ThemeData(colorSchemeSeed: Colors.indigo),
    darkTheme: ThemeData(
      colorSchemeSeed: Colors.indigo,
      brightness: Brightness.dark,
    ),
    home: Home(orvano: orvano),
  );
}
