/// Browsers forbid setting `User-Agent` (spec 0004 NET-6).
const bool sendsUserAgent = false;

/// The browser hides TLS failures behind a generic network error.
bool isTlsFailure(Object error) => false;
