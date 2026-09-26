import 'generated/events.dart';

/// Turns a raw JSON event payload into its typed model.
typedef EventDecoder = Object Function(Map<String, dynamic> json);

/// Decodes a raw realtime event payload into its typed model by event
/// [name]. Returns null for an event this SDK does not know yet (a newer
/// server), so you can ignore it safely. [registry] defaults to every event
/// in the contract.
Object? decodeEvent(
  String name,
  Object? raw, {
  Map<String, EventDecoder> registry = eventRegistry,
}) {
  final decode = registry[name];
  if (decode == null) return null;
  if (raw is! Map<String, dynamic>) {
    throw FormatException('The payload of event $name must be a JSON object');
  }
  return decode(raw);
}
