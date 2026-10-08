import 'dart:convert';
import 'dart:math';

import 'package:crypto/crypto.dart';
import 'package:http/http.dart' as http;
import 'package:orvano_core/orvano_core.dart' as core;
import 'package:orvano_dart/orvano_dart.dart' as srv;
import 'package:yaml/yaml.dart';

import 'dispatch_table.dart';
import 'generated/dispatch.dart';
import 'generated/test_client.dart';
import 'generated/test_events.dart';
import 'generated/test_models.dart';
import 'generated/test_server.dart' hide TestService;

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
/// `after`; `redeemLink` is the client SDK's link helper (spec 0010);
/// `oauthSignIn` runs `signInWithOAuth` or `linkIdentity` with a launcher
/// that follows the fake provider over HTTP, `oauthCode` stops at the code,
/// `createNonce` is `OrvanoNonce.create` (spec 0012), `totpCode` is an
/// authenticator app's current code, `completeMfa`, `verifyMfa`, and
/// `confirmTotp` are the client SDK's MFA helpers, and `registerPasskey` and
/// `signInWithPasskey` its passkey helpers, on the server's software
/// authenticator (spec 0013). Their names have no dot, so they never collide
/// with an operationId.
final Map<String, DispatchEntry> _runnerDispatch = {
  'totpCode': DispatchEntry(
    status: 200,
    client: (o, input) async => {
      'code': totpCode(
        '${input['secret']}',
        input['offset'] is int ? input['offset'] as int : 0,
      ),
    },
  ),
  'completeMfa': DispatchEntry(
    status: 201,
    client: (o, input) async {
      final user = await o.client.completeMfa(
        _mfaAnswer(input),
        authenticator: testPasskeys(o),
      );
      return {
        'user': user.toJson(),
        'isNewUser': false,
        'mfaRequired': false,
        'factors': <String>[],
      };
    },
  ),
  'verifyMfa': DispatchEntry(
    status: 200,
    client: (o, input) async {
      await o.client.verifyMfa(
        _mfaAnswer(input),
        authenticator: testPasskeys(o),
      );
      return {'verified': true};
    },
  ),
  'registerPasskey': DispatchEntry(
    status: 201,
    client: (o, input) async => (await o.client.registerPasskey(
      name: input['name'] as String?,
      authenticator: testPasskeys(o),
    )).toJson(),
  ),
  'signInWithPasskey': DispatchEntry(
    status: 201,
    client: (o, input) async {
      final user = await o.client.signInWithPasskey(
        authenticator: testPasskeys(o),
      );
      return {
        'user': user.toJson(),
        'isNewUser': false,
        'mfaRequired': false,
        'factors': <String>[],
      };
    },
  ),
  'confirmTotp': DispatchEntry(
    status: 200,
    client: (o, input) async => {
      'recoveryCodes': await o.client.confirmTotp('${input['code']}'),
    },
  ),
  'now': DispatchEntry(
    status: 200,
    client: (o, input) async => {'now': _now()},
    server: (o, input) async => {'now': _now()},
  ),
  'redeemLink': DispatchEntry(
    status: 200,
    client: (o, input) async {
      final result = await o.client.handleLink(
        Uri.parse('${input['url']}'),
        password: input['password'] as String?,
      );
      return _handled(result);
    },
  ),
  'createNonce': DispatchEntry(
    status: 200,
    client: (o, input) async {
      final nonce = core.OrvanoNonce.create();
      return {'raw': nonce.raw, 'hashed': nonce.hashed};
    },
  ),
  'oauthSignIn': DispatchEntry(
    status: 200,
    client: (o, input) async {
      final redirectUrl = _redirectUrl(input, o.client.endpoint);
      final provider = core.OAuthProvider.fromJson('${input['provider']}');
      Future<Uri> launcher(Uri url, Uri back) =>
          _followOAuth(url, input['testUser'], back, o.client.endpoint);
      return _handled(
        input['link'] == true
            ? await o.client.linkIdentity(
                provider,
                redirectUrl: redirectUrl,
                launcher: launcher,
              )
            : await o.client.signInWithOAuth(
                provider,
                redirectUrl: redirectUrl,
                launcher: launcher,
              ),
      );
    },
  ),
  'oauthCode': DispatchEntry(
    status: 200,
    client: (o, input) async {
      final redirectUrl = _redirectUrl(input, o.client.endpoint);
      final verifier = _base64Url(
        List<int>.generate(32, (_) => Random.secure().nextInt(256)),
      );
      final flow = await core.AccountService(o.client).createOAuthFlow(
        core.CreateOAuthFlowRequest(
          provider: core.OAuthProvider.fromJson('${input['provider']}'),
          redirectUrl: redirectUrl.toString(),
          codeChallenge: _s256(verifier),
        ),
      );
      final back = await _followOAuth(
        Uri.parse(flow.url),
        input['testUser'],
        redirectUrl,
        o.client.endpoint,
      );
      return {
        'type': back.queryParameters['orvano_type'],
        'code': back.queryParameters['orvano_code'],
        'error': back.queryParameters['orvano_error'],
        'codeVerifier': verifier,
      };
    },
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

const _base32Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

/// The 6 digit code an authenticator app shows for [secret] (unpadded
/// base32) at this 30 second step plus [offset] (RFC 6238, HMAC SHA-1).
String totpCode(String secret, int offset) {
  final key = <int>[];
  var buffer = 0;
  var bits = 0;
  for (final c in secret.split('')) {
    buffer = ((buffer << 5) | _base32Alphabet.indexOf(c)) & 0xffff;
    bits += 5;
    if (bits >= 8) {
      key.add((buffer >> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  final step = DateTime.now().millisecondsSinceEpoch ~/ 1000 ~/ 30 + offset;
  // The step fits in 32 bits, and shifts past 31 differ on the web.
  final counter = [
    0,
    0,
    0,
    0,
    for (final s in [24, 16, 8, 0]) (step >> s) & 0xff,
  ];
  final mac = Hmac(sha1, key).convert(counter).bytes;
  final at = mac[19] & 0x0f;
  final binary =
      ((mac[at] & 0x7f) << 24) |
      (mac[at + 1] << 16) |
      (mac[at + 2] << 8) |
      mac[at + 3];
  return (binary % 1000000).toString().padLeft(6, '0');
}

/// What `handleLink`, `signInWithOAuth`, or `linkIdentity` did, as the JS
/// runner reports it.
/// The one factor of an MFA runner step: `totpCode`, `recoveryCode`, or
/// `passkey: true`.
core.MfaAnswer _mfaAnswer(Map<String, Object?> input) => switch (input) {
  {'passkey': true} => const core.MfaAnswer.passkey(),
  {'totpCode': final String code} => core.MfaAnswer.totp(code),
  _ => core.MfaAnswer.recoveryCode('${input['recoveryCode']}'),
};

/// The origin the software authenticator names in its client data: a web
/// platform of the scenario project (`localhost`, any port).
const passkeyOrigin = 'http://localhost:3000';

final _testPasskeys = Expando<TestPasskeys>('orvano.scenarios.passkeys');

/// The [TestPasskeys] of [surface]'s client, made once per surface.
TestPasskeys testPasskeys(ClientSurface surface) =>
    _testPasskeys[surface] ??= TestPasskeys(surface.test);

/// A [core.PasskeyAuthenticator] on the server's `Test` only software
/// authenticator (spec 0013, AC-45): `create` asks
/// `test.createPasskeyCredential` for a new passkey, and `get` signs with the
/// newest one the options allow (any, for a sign in). It holds only the
/// credential IDs; the keys live in the server for the run.
final class TestPasskeys implements core.PasskeyAuthenticator {
  /// Creates the authenticator over [test].
  TestPasskeys(this.test);

  /// The runner's test service.
  final TestService test;
  final _made = <String>[];

  @override
  Future<core.PasskeyRegistrationCredential> create(
    core.PasskeyCreationOptions options,
  ) async {
    final credential = await test.createPasskeyCredential(
      TestCreatePasskeyCredentialRequest(
        options: options,
        origin: passkeyOrigin,
      ),
    );
    _made.add(credential.id);
    return credential;
  }

  @override
  Future<core.PasskeyAssertionCredential> get(
    core.PasskeyRequestOptions options, {
    bool autofill = false,
  }) {
    final allowed = {for (final c in options.allowCredentials) c.id};
    final id = _made.reversed
        .where((m) => allowed.isEmpty || allowed.contains(m))
        .firstOrNull;
    if (id == null) {
      throw StateError(
        'the test authenticator made no passkey these options allow',
      );
    }
    return test.createPasskeyAssertion(
      TestCreatePasskeyAssertionRequest(
        options: options,
        origin: passkeyOrigin,
        credentialId: id,
      ),
    );
  }
}

Map<String, Object?>? _handled(core.HandledLink? result) => switch (result) {
  null => null,
  core.LinkResult(:final type, :final user, :final isNewUser) => {
    'type': type.wire,
    'user': user?.toJson(),
    'isNewUser': isNewUser,
    'mfaRequired': result.mfaRequired,
    'factors': [for (final f in result.factors) f.value],
  },
  core.OAuthSignInResult(:final user, :final isNewUser) => {
    'type': 'oauth',
    'user': user?.toJson(),
    'isNewUser': isNewUser,
    'mfaRequired': result.mfaRequired,
    'factors': [for (final f in result.factors) f.value],
  },
  core.IdentityLinkResult(:final identity) => {
    'type': 'oauth_link',
    'identity': identity.toJson(),
  },
};

/// Where the fake provider's flows send the browser back (spec 0012). Outside
/// a browser nothing loads it; in a browser the last hop is fetched, so it is
/// the server's own health route on `localhost` (a web platform of the
/// fixture project), whose final URL the response reports.
Uri _redirectUrl(Map<String, Object?> input, String endpoint) {
  if (input['redirectUrl'] case final String url) return Uri.parse(url);
  if (!_inBrowser) return Uri.parse('http://localhost:3000/auth/callback');
  final server = Uri.parse(endpoint);
  return Uri.parse('http://localhost:${server.port}/v1/health');
}

/// Follows the fake provider the way a browser would, over HTTP (spec 0012,
/// AC-24): adds `test_user`, follows redirects and Apple's form post, and
/// returns the URL that leaves for [redirectUrl]. URLs on the server's own
/// port at `localhost` are reached through [endpoint] (`10.0.2.2` on
/// Android).
Future<Uri> _followOAuth(
  Uri url,
  Object? testUser,
  Uri redirectUrl,
  String endpoint,
) async {
  final server = Uri.parse(endpoint);
  final client = http.Client();
  try {
    var next = url.replace(
      queryParameters: {
        ...url.queryParameters,
        'test_user': _base64Url(utf8.encode(jsonEncode(testUser))),
      },
    );
    var method = 'GET';
    String? body;
    for (var hop = 0; hop < 10; hop++) {
      final local =
          (next.host == 'localhost' || next.host == '127.0.0.1') &&
          next.port == server.port;
      final target = local
          ? next.replace(host: server.host, port: server.port)
          : next;
      // A browser can't hand back a redirect (the fetch fails), so there it
      // follows them and reports the final URL; elsewhere each hop is read.
      final request = http.Request(method, target)
        ..followRedirects = _inBrowser;
      if (body != null) {
        request.headers['content-type'] = 'application/x-www-form-urlencoded';
        request.body = body;
      }
      final response = await client.send(request);
      await response.stream.drain<void>();
      if (response case http.BaseResponseWithUrl(
        :final url,
      ) when _inBrowser && url.toString().startsWith('$redirectUrl')) {
        return url;
      }
      final location = response.headers['location'];
      if (location != null &&
          response.statusCode >= 300 &&
          response.statusCode < 400) {
        final to = next.resolve(location);
        if (to.toString().startsWith('$redirectUrl')) return to;
        next = to;
        method = 'GET';
        body = null;
        continue;
      }
      final action = response.headers['x-orvano-test-form-action'];
      final form = response.headers['x-orvano-test-form-body'];
      if (action != null && form != null) {
        next = next.resolve(action);
        method = 'POST';
        body = form;
        continue;
      }
      throw _StepFailure('the provider flow stopped at ${response.statusCode}');
    }
    throw _StepFailure('the provider flow redirected more than 10 times');
  } finally {
    client.close();
  }
}

String _base64Url(List<int> bytes) =>
    base64Url.encode(bytes).replaceAll('=', '');

/// PKCE's S256 challenge of [verifier].
String _s256(String verifier) =>
    _base64Url(sha256.convert(ascii.encode(verifier)).bytes);

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
    final index = int.tryParse(key);
    if (current is Map<String, Object?>) {
      current = current[key];
    } else if (current is List<Object?> &&
        index != null &&
        index >= 0 &&
        index < current.length) {
      // A number picks an item of a list (spec 0012's identity scenarios).
      current = current[index];
    } else {
      throw _StepFailure('save path $path not found');
    }
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
