// Runs every shared scenario through orvano_flutter on the device under test.
// The endpoint comes from --dart-define=ORVANO_ENDPOINT=...; an Android
// emulator reaches the host at http://10.0.2.2:8080.
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:orvano_flutter/orvano_flutter.dart';
import 'package:orvano_scenarios/orvano_scenarios.dart';

const endpoint = String.fromEnvironment(
  'ORVANO_ENDPOINT',
  defaultValue: 'http://localhost:8080',
);

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('shared scenarios pass through orvano_flutter', (tester) async {
    final manifest = await AssetManifest.loadFromAssetBundle(rootBundle);
    final files =
        manifest
            .listAssets()
            .where((a) => a.startsWith('assets/scenarios/'))
            .where((a) => a.endsWith('.yaml'))
            .where((a) => !a.endsWith('fixtures.yaml'))
            .toList()
          ..sort();
    expect(files, isNotEmpty, reason: 'run tool/copy_scenarios.dart first');

    final scenarios = [
      for (final f in files) parseScenario(await rootBundle.loadString(f)),
    ];
    final surface = Surface.connect(
      endpoint,
      client: Client(endpoint: endpoint),
    );
    final results = await runScenarios(scenarios, surface);
    surface.close();

    for (final r in results) {
      debugPrint(r.toString());
    }
    final failed = results.where((r) => r.outcome == 'failed');
    expect(failed, isEmpty, reason: failed.join('\n'));
    expect(results.where((r) => r.outcome == 'passed'), isNotEmpty);
  });
}
