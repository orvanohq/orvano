import 'dart:convert';

import 'package:orvano_core/orvano_core.dart' as core;
import 'package:orvano_dart/orvano_dart.dart' as srv;
import 'package:yaml/yaml.dart';

import 'dispatch_table.dart';
import 'generated/dispatch.dart';

/// What happened to one scenario.
final class ScenarioResult {
  const ScenarioResult(this.name, this.outcome, [this.reason]);

  final String name;

  /// `passed`, `failed`, or `skipped`.
  final String outcome;

  /// Why it failed or was skipped.
  final String? reason;

  @override
  String toString() =>
      '${outcome.padRight(7)} $name${reason == null ? '' : ': $reason'}';
}

/// The SDK objects a surface offers, one per role.
final class Surface {
  const Surface({required this.client, required this.server});

  final core.Orvano client;
  final srv.Orvano server;
}

/// Parses one scenario file into plain JSON values.
Map<String, Object?> parseScenario(String yamlText) =>
    jsonDecode(jsonEncode(loadYaml(yamlText))) as Map<String, Object?>;

/// Runs every scenario in order and reports each one. Never throws for a
/// scenario failure.
Future<List<ScenarioResult>> runScenarios(
  List<Map<String, Object?>> scenarios,
  Surface surface,
) async {
  final results = <ScenarioResult>[];
  for (final scenario in scenarios) {
    final name = scenario['name'] as String;
    try {
      await _runScenario(scenario, surface);
      results.add(ScenarioResult(name, 'passed'));
    } on _Skipped catch (e) {
      results.add(ScenarioResult(name, 'skipped', e.reason));
    } on _StepFailure catch (e) {
      results.add(ScenarioResult(name, 'failed', e.reason));
    } on Object catch (e) {
      results.add(ScenarioResult(name, 'failed', '$e'));
    }
  }
  return results;
}

final class _StepFailure implements Exception {
  _StepFailure(this.reason);
  final String reason;
}

final class _Skipped implements Exception {
  _Skipped(this.reason);
  final String reason;
}

Future<void> _runScenario(
  Map<String, Object?> scenario,
  Surface surface,
) async {
  final vars = <String, Object?>{};
  final steps = (scenario['steps'] as List<Object?>)
      .cast<Map<String, Object?>>();
  for (final (index, step) in steps.indexed) {
    final op = step['op'] as String;
    final role = step['as'] as String;
    final where = 'step ${index + 1} ($op as $role)';
    final entry = dispatch[op];
    if (entry == null) {
      throw _StepFailure('$where: the contract has no operation $op');
    }

    final input =
        _substitute(step['input'] ?? <String, Object?>{}, vars)
            as Map<String, Object?>;
    final expect = _substitute(step['expect'], vars) as Map<String, Object?>;

    int status;
    String? code;
    Object? body;
    try {
      body = await _call(op, role, entry, surface, input);
      status = entry.status;
    } on core.OrvanoException catch (e) {
      status = e.status;
      code = e.code;
    }

    if (status != expect['status']) {
      throw _StepFailure(
        '$where: expected status ${expect['status']}, got $status'
        '${code == null ? '' : ' ($code)'}',
      );
    }
    if (expect['code'] case final String expected when expected != code) {
      throw _StepFailure('$where: expected code $expected, got $code');
    }
    if (expect.containsKey('body')) {
      final mismatch = _subsetMismatch(expect['body'], body, r'$');
      if (mismatch != null) throw _StepFailure('$where: body $mismatch');
    }
    final save = (step['save'] as Map<String, Object?>?) ?? const {};
    for (final MapEntry(:key, :value) in save.entries) {
      vars[key] = _select(body, value as String);
    }
  }
}

Future<Object?> _call(
  String op,
  String role,
  DispatchEntry entry,
  Surface surface,
  ScenarioInput input,
) {
  switch (role) {
    case 'client':
      final call = entry.client;
      if (call == null) throw _Skipped('$op has no client call in orvano_core');
      return call(surface.client, input);
    case 'server':
      final call = entry.server;
      if (call == null) throw _Skipped('$op has no server call in orvano_dart');
      return call(surface.server, input);
    case 'console':
      throw _Skipped('console steps run only in the JS interpreter');
    default:
      throw _StepFailure('unknown role $role');
  }
}

final _whole = RegExp(r'^\$\{(\w+)\}$');
final _embedded = RegExp(r'\$\{(\w+)\}');

Object? _substitute(Object? value, Map<String, Object?> vars) {
  Object? lookup(String name) {
    if (!vars.containsKey(name)) {
      throw _StepFailure('\${$name} was never saved by an earlier step');
    }
    return vars[name];
  }

  return switch (value) {
    final String s when _whole.hasMatch(s) => lookup(
      _whole.firstMatch(s)!.group(1)!,
    ),
    final String s => s.replaceAllMapped(
      _embedded,
      (m) => '${lookup(m.group(1)!)}',
    ),
    final List<Object?> l => [for (final v in l) _substitute(v, vars)],
    final Map<String, Object?> m => {
      for (final MapEntry(:key, :value) in m.entries)
        key: _substitute(value, vars),
    },
    _ => value,
  };
}

Object? _select(Object? body, String path) {
  if (!path.startsWith(r'$')) {
    throw _StepFailure('save path $path must start with \$');
  }
  var current = body;
  for (final key in path.substring(1).split('.').where((k) => k.isNotEmpty)) {
    if (current is! Map<String, Object?>) {
      throw _StepFailure('save path $path not found');
    }
    current = current[key];
  }
  return current;
}

String? _subsetMismatch(Object? expected, Object? actual, String at) {
  switch (expected) {
    case final List<Object?> list:
      if (actual is! List<Object?> || actual.length != list.length) {
        return '$at: expected ${list.length} items';
      }
      for (final (i, item) in list.indexed) {
        final mismatch = _subsetMismatch(item, actual[i], '$at[$i]');
        if (mismatch != null) return mismatch;
      }
      return null;
    case final Map<String, Object?> map:
      if (actual is! Map<String, Object?>) return '$at: expected an object';
      for (final MapEntry(:key, :value) in map.entries) {
        final mismatch = _subsetMismatch(value, actual[key], '$at.$key');
        if (mismatch != null) return mismatch;
      }
      return null;
    default:
      return expected == actual
          ? null
          : '$at: expected ${jsonEncode(expected)}, got ${jsonEncode(actual)}';
  }
}
