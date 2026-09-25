import 'package:orvano_core/orvano_core.dart' as core;
import 'package:orvano_dart/orvano_dart.dart' as srv;

/// A step's `input`: path and query parameters by name, the JSON body under
/// `body`.
typedef ScenarioInput = Map<String, Object?>;

/// One operation in the generated dispatch table.
final class DispatchEntry {
  const DispatchEntry({required this.status, this.client, this.server});

  /// The success status the contract declares; the SDK returns the body.
  final int status;

  /// The call through `orvano_core` (`client` and `both` operations).
  final Future<Object?> Function(core.Orvano orvano, ScenarioInput input)?
  client;

  /// The call through `orvano_dart` (`server` and `both` operations).
  final Future<Object?> Function(srv.Orvano orvano, ScenarioInput input)?
  server;
}
