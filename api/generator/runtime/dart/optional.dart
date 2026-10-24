// A field of a partial update (spec 0014 JSW-9, JSW-10).

/// A field of a partial update: [Optional.absent] (the default) leaves it
/// unchanged and is not sent, and [Optional.of] sets it. `Optional.of(null)`
/// clears a field that can be null.
sealed class Optional<T> {
  const Optional._();

  /// Sets the field to [value].
  const factory Optional.of(T value) = _Present<T>;

  /// Leaves the field as it is; it is not sent.
  const factory Optional.absent() = _Absent<T>;

  /// Whether the field is set, so it is sent.
  bool get isPresent;

  /// The value set. Throws [StateError] when the field is absent.
  T get value;
}

final class _Present<T> extends Optional<T> {
  const _Present(this.value) : super._();

  @override
  final T value;

  @override
  bool get isPresent => true;

  @override
  bool operator ==(Object other) => other is _Present<T> && other.value == value;

  @override
  int get hashCode => value.hashCode;

  @override
  String toString() => 'Optional.of($value)';
}

final class _Absent<T> extends Optional<T> {
  const _Absent() : super._();

  @override
  T get value => throw StateError('The field is absent; check isPresent.');

  @override
  bool get isPresent => false;

  @override
  bool operator ==(Object other) => other is _Absent<T>;

  @override
  int get hashCode => 0;

  @override
  String toString() => 'Optional.absent()';
}
