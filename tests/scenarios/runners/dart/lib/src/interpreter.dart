import 'dart:convert';
import 'dart:math';

import 'package:orvano_core/orvano_core.dart' as core;
import 'package:orvano_dart/orvano_dart.dart' as srv;
import 'package:yaml/yaml.dart';

import 'dispatch_table.dart';
import 'generated/dispatch.dart';
import 'generated/test_client.dart';
import 'generated/test_events.dart';
import 'generated/test_server.dart';

const _inBrowser = bool.fromEnvironment('dart.library.js_interop');

/// The package's events plus the test events, as the spec has runners decode
/// them.
const _events = {...core.eventRegistry, ...testEventRegistry};

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
  /// Wraps SDK objects the caller built. Without [serverKey] (a browser
  /// can't hold an API key), server steps that need a scope skip.
  const Surface({
    required this.client,
    required this.server,
    this.serverKey = false,
  });

  /// The client and server SDK objects for [endpoint], as every Dart runner
  /// builds them: both send [project] (the fixture project), and the server
  /// one sends [apiKey] (the fixture key), except in a browser, where setting
  /// a key throws. Pass [client] to run the client steps through a client
  /// built elsewhere (the Flutter runner's, from `orvano_flutter`).
  factory Surface.connect(
    String endpoint, {
    core.Client? client,
    String? project,
    String? apiKey,
  }) {
    final key = _inBrowser ? null : apiKey;
    return Surface(
      client: ClientSurface(
        client ?? core.Client(endpoint: endpoint, project: project),
      ),
      server: ServerSurface(
        srv.Client(endpoint: endpoint, project: project, apiKey: key),
      ),
      serverKey: key != null,
    );
  }

  /// `orvano_core` plus the test services.
  final ClientSurface client;

  /// `orvano_dart` plus the test services.
  final ServerSurface server;

  /// Whether the server SDK sends an API key.
  final bool serverKey;

  /// Closes both clients' connections.
  void close() {
    client.client.close();
    server.client.close();
  }
}

Object? _fixtures(String fixturesYaml) =>
    jsonDecode(jsonEncode(loadYaml(fixturesYaml)));

/// The first project in `fixtures.yaml`, which the server seeds in the Test
/// environment; null when there is none.
String? fixtureProject(String fixturesYaml) =>
    switch (_fixtures(fixturesYaml)) {
      {'projects': [{'id': final String id}, ...]} => id,
      _ => null,
    };

/// The first API key of the first project in `fixtures.yaml`; null when
/// there is none.
String? fixtureApiKey(String fixturesYaml) {
  final fixtures = _fixtures(fixturesYaml);
  final project = fixtureProject(fixturesYaml);
  if (fixtures case {'apiKeys': final List<Object?> keys}) {
    for (final key in keys) {
      if (key case {
        'project': final String p,
        'secret': final String secret,
      } when p == project) {
        return secret;
      }
    }
  }
  return null;
}

/// Runner operations: calls the scenarios make that are not contract
/// operations. `signIn` is a plain sign in call that leaves the SDK's stored
/// session alone, so a runner without client operations (.NET) can get a
/// token too; `verifyAccessToken` is the server SDK's own check; `now` is the
/// runner's clock, saved before a send and passed to `test.getLatestEmail` as
/// `after`. Their names have no dot, so they never collide with an operationId.
final Map<String, DispatchEntry> _runnerDispatch = {
  'now': DispatchEntry(
    status: 200,
    client: (o, input) async => {'now': _now()},
    server: (o, input) async => {'now': _now()},
  ),
  'signIn': DispatchEntry(
    status: 201,
    client: (o, input) => o.client.send(
      'POST',
      '/v1/account/sessions/password',
      body: input['body'],
    ),
  ),
  'verifyAccessToken': DispatchEntry(
    status: 200,
    server: (o, input) async {
      final verified = await (o.client as srv.Client).verifyAccessToken(
        '${input['token']}',
        online: input['online'] == true,
      );
      return {
        'userId': verified.userId,
        'sessionId': verified.sessionId,
        'emailVerified': verified.emailVerified,
        'expiresAt': verified.expiresAt.toIso8601String(),
      };
    },
  ),
};

String _now() => DateTime.now().toUtc().toIso8601String();

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
  // `${unique}` is fresh per scenario run, so runs never collide on unique
  // values such as emails.
  final vars = <String, Object?>{'unique': _uniqueValue()};
  final steps = (scenario['steps'] as List<Object?>)
      .cast<Map<String, Object?>>();
  for (final (index, step) in steps.indexed) {
    final op = step['op'] as String?;
    final event = step['event'] as String?;
    final role = step['as'] as String?;
    final where =
        'step ${index + 1} (${op ?? 'event ${event ?? '?'}'}'
        '${role == null ? '' : ' as $role'})';
    final expect = _substitute(step['expect'], vars) as Map<String, Object?>;

    int? status;
    String? code;
    Object? body;
    if (event != null) {
      final decoded = core.decodeEvent(
        event,
        _substitute(step['raw'], vars),
        registry: _events,
      );
      if (decoded == null) {
        throw _StepFailure('$where: the contract has no event $event');
      }
      body = jsonDecode(jsonEncode(decoded));
    } else {
      // Console operations are not in this dispatch table, so skip first.
      if (role == 'console') {
        throw _Skipped('console steps run only in the JS interpreter');
      }
      final entry = op == null ? null : _runnerDispatch[op] ?? dispatch[op];
      if (entry == null) {
        throw _StepFailure('$where: the contract has no operation $op');
      }
      final input =
          _substitute(step['input'] ?? <String, Object?>{}, vars)
              as Map<String, Object?>;
      try {
        body = await _call(
          op!,
          role,
          step['paginate'] == true,
          entry,
          surface,
          input,
        );
        status = entry.status;
      } on core.OrvanoException catch (e) {
        status = e.status;
        code = e.code;
      }
    }

    if (expect['status'] case final int expected when expected != status) {
      throw _StepFailure(
        '$where: expected status $expected, got $status'
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
  String? role,
  bool all,
  DispatchEntry entry,
  Surface surface,
  ScenarioInput input,
) async {
  _Skipped missing(String sdk) =>
      _Skipped('$op has no ${all ? 'paged ' : ''}$role call in $sdk');
  switch (role) {
    case 'client':
      if (all) {
        final call = entry.clientAll ?? (throw missing('orvano_core'));
        return {'items': await call(surface.client, input).toList()};
      }
      final call = entry.client ?? (throw missing('orvano_core'));
      return call(surface.client, input);
    case 'server':
      if (entry.scope != null && !surface.serverKey) {
        throw _Skipped(
          '$op needs an API key, which this surface does not hold',
        );
      }
      if (all) {
        final call = entry.serverAll ?? (throw missing('orvano_dart'));
        return {'items': await call(surface.server, input).toList()};
      }
      final call = entry.server ?? (throw missing('orvano_dart'));
      return call(surface.server, input);
    case null:
      throw _StepFailure('$op: an operation step needs `as`');
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

final _random = Random.secure();

/// Twelve lowercase letters and digits, random per call.
String _uniqueValue() => String.fromCharCodes([
  for (var i = 0; i < 12; i++)
    '0123456789abcdefghijklmnopqrstuvwxyz'.codeUnitAt(_random.nextInt(36)),
]);
