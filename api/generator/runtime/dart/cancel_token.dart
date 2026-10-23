import 'dart:async';

import 'package:meta/meta.dart';

/// Why the SDK cancelled a call by itself.
enum OrvanoCancelReason {
  /// The client was closed while the call was in flight (spec 0004 CLN-5).
  clientClosed,
}

/// Cancels one or more Orvano calls (spec 0004 NET-5).
///
/// Pass it in `OrvanoRequestOptions(cancelToken: ...)`. One token may be
/// passed to many calls.
final class OrvanoCancelToken {
  final Completer<void> _cancelled = Completer<void>();
  final Set<void Function()> _callbacks = {};
  Object? _reason;

  /// Whether [cancel] has been called.
  bool get isCancelled => _cancelled.isCompleted;

  /// The reason given to [cancel], if any.
  Object? get reason => _reason;

  /// Completes when [cancel] is called.
  Future<void> get whenCancelled => _cancelled.future;

  /// Cancels every call using this token. Later calls do nothing.
  void cancel([Object? reason]) {
    if (_cancelled.isCompleted) return;
    _reason = reason;
    _cancelled.complete();
    for (final callback in [..._callbacks]) {
      callback();
    }
    _callbacks.clear();
  }

  /// Runs [onCancel] when this token is cancelled. Call the returned function
  /// to stop listening.
  @internal
  void Function() register(void Function() onCancel) {
    _callbacks.add(onCancel);
    return () => _callbacks.remove(onCancel);
  }
}
