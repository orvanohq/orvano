import 'generated/test_client.dart';
import 'generated/test_server.dart';

/// A step's `input`: path and query parameters by name, the JSON body under
/// `body`.
typedef ScenarioInput = Map<String, Object?>;

/// One operation in the generated dispatch table.
final class DispatchEntry {
  const DispatchEntry({
    required this.status,
    this.client,
    this.server,
    this.clientAll,
    this.serverAll,
  });

  /// The success status the contract declares; the SDK returns the body.
  final int status;

  /// The call through `orvano_core` (`client` and `both` operations).
  final Future<Object?> Function(ClientSurface orvano, ScenarioInput input)?
  client;

  /// The call through `orvano_dart` (`server` and `both` operations).
  final Future<Object?> Function(ServerSurface orvano, ScenarioInput input)?
  server;

  /// For a list operation: every item through `orvano_core`'s stream.
  final Stream<Object?> Function(ClientSurface orvano, ScenarioInput input)?
  clientAll;

  /// For a list operation: every item through `orvano_dart`'s stream.
  final Stream<Object?> Function(ServerSurface orvano, ScenarioInput input)?
  serverAll;
}
