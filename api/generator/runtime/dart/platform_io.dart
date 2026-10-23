import 'dart:io';

/// Native platforms send a `User-Agent` (spec 0004 NET-6).
const bool sendsUserAgent = true;

/// A TLS or certificate failure, which is never retried (spec 0004 NET-3).
bool isTlsFailure(Object error) => error is TlsException;
