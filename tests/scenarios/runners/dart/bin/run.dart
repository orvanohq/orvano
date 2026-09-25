// Runs the shared scenarios through orvano_dart against the Orvano at
// ORVANO_ENDPOINT (default http://localhost:8080).
import 'dart:io';

import 'package:orvano_core/orvano_core.dart' as core;
import 'package:orvano_dart/orvano_dart.dart' as srv;
import 'package:orvano_scenarios/orvano_scenarios.dart';

Future<void> main() async {
  final endpoint =
      Platform.environment['ORVANO_ENDPOINT'] ?? 'http://localhost:8080';
  final dir = Directory.fromUri(Platform.script.resolve('../../..'));
  final files =
      dir
          .listSync()
          .whereType<File>()
          .where(
            (f) =>
                f.path.endsWith('.yaml') && !f.path.endsWith('fixtures.yaml'),
          )
          .toList()
        ..sort((a, b) => a.path.compareTo(b.path));
  final scenarios = [
    for (final f in files) parseScenario(f.readAsStringSync()),
  ];

  final client = core.Client(endpoint: endpoint);
  final serverClient = srv.Client(endpoint: endpoint);
  final results = await runScenarios(
    scenarios,
    Surface(client: core.Orvano(client), server: srv.Orvano(serverClient)),
  );
  client.close();
  serverClient.close();

  results.forEach(print);
  final failed = results.where((r) => r.outcome == 'failed').length;
  final passed = results.where((r) => r.outcome == 'passed').length;
  print(
    'dart: $passed passed, $failed failed, '
    '${results.length - passed - failed} skipped',
  );
  exitCode = failed > 0 || passed == 0 ? 1 : 0;
}
