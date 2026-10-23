/// Turns JSON objects into your own type and back, for typed rows
/// (spec 0004 MDL-5).
abstract interface class OrvanoCodec<T> {
  /// A codec from two functions.
  factory OrvanoCodec.of({
    required T Function(Map<String, Object?> json) fromJson,
    required Map<String, Object?> Function(T value) toJson,
  }) = _FunctionCodec<T>;

  /// Reads a value from its JSON object.
  T fromJson(Map<String, Object?> json);

  /// Writes a value as a JSON object.
  Map<String, Object?> toJson(T value);
}

final class _FunctionCodec<T> implements OrvanoCodec<T> {
  _FunctionCodec({
    required T Function(Map<String, Object?> json) fromJson,
    required Map<String, Object?> Function(T value) toJson,
  }) : _fromJson = fromJson,
       _toJson = toJson;

  final T Function(Map<String, Object?> json) _fromJson;
  final Map<String, Object?> Function(T value) _toJson;

  @override
  T fromJson(Map<String, Object?> json) => _fromJson(json);

  @override
  Map<String, Object?> toJson(T value) => _toJson(value);
}
