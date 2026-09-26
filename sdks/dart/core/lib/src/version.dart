import 'generated/version.dart';

/// The header every request carries: the SDK's name and version, for example
/// `orvano_core/0.4.2`.
const sdkHeader = 'X-Orvano-SDK';

/// The header every response carries: the server's Orvano version.
const serverVersionHeader = 'X-Orvano-Version';

/// The warning for a server whose major.minor differs from this SDK's, or
/// null when they match or the server sent no version. SDK `0.4.x` targets
/// Orvano `0.4`.
String? versionMismatch(
  String sdkName,
  String? serverVersion,
  String endpoint,
) {
  if (serverVersion == null || serverVersion.isEmpty) return null;
  if (_majorMinor(serverVersion) == _majorMinor(sdkVersion)) return null;
  return '$sdkName $sdkVersion targets Orvano ${_majorMinor(sdkVersion)}, '
      'but the server at $endpoint runs $serverVersion. Calls still work; '
      'use $sdkName ${_majorMinor(serverVersion)}.x to match.';
}

String _majorMinor(String version) => version.split('.').take(2).join('.');
