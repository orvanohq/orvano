// Copies the shared scenarios into this app's assets, since a device can't
// read the host's files. Run from this directory: dart run tool/copy_scenarios.dart
import 'dart:io';

void main() {
  final source = Directory.fromUri(Platform.script.resolve('../../..'));
  final target = Directory.fromUri(
    Platform.script.resolve('../assets/scenarios'),
  )..createSync(recursive: true);
  for (final old in target.listSync().whereType<File>()) {
    if (old.path.endsWith('.yaml')) old.deleteSync();
  }
  for (final file in source.listSync().whereType<File>()) {
    if (!file.path.endsWith('.yaml')) continue;
    file.copySync('${target.path}/${file.uri.pathSegments.last}');
  }
}
