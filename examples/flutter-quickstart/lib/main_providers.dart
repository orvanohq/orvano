import 'package:flutter/material.dart';
import 'package:orvano_flutter/orvano_flutter.dart';

import 'config.dart';
import 'providers.dart';

/// The quickstart with provider sign in:
/// `flutter run -t lib/main_providers.dart --dart-define=ORVANO_PROJECT=...`,
/// plus `GOOGLE_WEB_CLIENT_ID` and `GOOGLE_IOS_CLIENT_ID` for native Google
/// sign in.
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  if (project.isEmpty) {
    throw StateError(
      'Run with --dart-define=ORVANO_PROJECT=<your project ID>.',
    );
  }
  final orvano = Orvano(createClient(endpoint: endpoint, project: project));
  runApp(
    MaterialApp(
      title: 'Orvano Flutter quickstart',
      theme: ThemeData(colorSchemeSeed: Colors.indigo),
      darkTheme: ThemeData(
        colorSchemeSeed: Colors.indigo,
        brightness: Brightness.dark,
      ),
      home: ProvidersScreen(orvano: orvano),
    ),
  );
}
