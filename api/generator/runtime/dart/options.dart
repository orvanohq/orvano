import 'cancel_token.dart';

/// Options for every call a client makes (spec 0004 CLN-8).
final class OrvanoClientOptions {
  /// Creates client options; every field has a default.
  const OrvanoClientOptions({
    this.timeout = const Duration(seconds: 30),
    this.retry = const RetryOptions(),
    this.defaultHeaders = const {},
    this.allowInsecureEndpoint = false,
  });

  /// The per attempt timeout (NET-4). Default 30 seconds.
  final Duration timeout;

  /// How idempotent calls are retried (NET-3).
  final RetryOptions retry;

  /// Headers added to every request. They can't set the credential, API
  /// version, or SDK identity headers.
  final Map<String, String> defaultHeaders;

  /// Allows `http` for any host, for a server on your own network or the
  /// Android emulator's `10.0.2.2` (NET-7). Default false.
  final bool allowInsecureEndpoint;
}

/// How idempotent calls are retried (spec 0004 NET-3).
final class RetryOptions {
  /// Creates retry options; `RetryOptions(maxRetries: 0)` turns retry off.
  const RetryOptions({
    this.maxRetries = 2,
    this.baseDelay = const Duration(milliseconds: 500),
    this.maxRetryAfter = const Duration(seconds: 30),
  });

  /// Retries after the first attempt, 0 to 2. Default 2.
  final int maxRetries;

  /// The first backoff; each retry doubles it, with full jitter.
  final Duration baseDelay;

  /// The longest `Retry-After` the SDK waits for; a longer one throws.
  final Duration maxRetryAfter;
}

/// Options for one call. A null field uses the client's value (CLN-8).
final class OrvanoRequestOptions {
  /// Creates per call options.
  const OrvanoRequestOptions({
    this.timeout,
    this.retry,
    this.cancelToken,
    this.headers,
  });

  /// The per attempt timeout for this call.
  final Duration? timeout;

  /// The retry rules for this call.
  final RetryOptions? retry;

  /// Cancels this call when cancelled.
  final OrvanoCancelToken? cancelToken;

  /// Headers for this call; they override the client's default headers.
  final Map<String, String>? headers;
}
