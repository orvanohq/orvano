import 'package:orvano_core/orvano_core.dart';
import 'package:passkeys/authenticator.dart' as platform;
import 'package:passkeys/types.dart';

/// A [PasskeyAuthenticator] on the `passkeys` package (spec 0013, AC-38),
/// which uses `ASAuthorization` on iOS and macOS, Credential Manager on
/// Android, and Corbado's web plugin on the web. Pass it to `orvano_flutter`'s
/// `createClient` as `passkeys: PlatformPasskeys()`.
///
/// The app sets up its side once: an Associated Domains entry
/// `webcredentials:<rpId>` on iOS and macOS, and on Android a
/// `/.well-known/assetlinks.json` on the RP ID's site that names the app's
/// signing certificate (the project's Passkeys card gives both snippets). A
/// web build loads Corbado's `bundle.js`, the version matching the resolved
/// `passkeys_web`, from the app's own `web/` folder in `web/index.html`, or it
/// fails at startup; see this package's README.
final class PlatformPasskeys implements PasskeyAuthenticator {
  /// Creates the authenticator; [debugMode] turns on the `passkeys` package's
  /// setup checks.
  PlatformPasskeys({bool debugMode = false})
    : _authenticator = platform.PasskeyAuthenticator(debugMode: debugMode);

  final platform.PasskeyAuthenticator _authenticator;

  @override
  Future<PasskeyRegistrationCredential> create(
    PasskeyCreationOptions options,
  ) async {
    final made = await _authenticator.register(
      RegisterRequestType.fromJson(
        _withTransports(options.toJson(), 'excludeCredentials'),
      ),
    );
    return PasskeyRegistrationCredential(
      id: made.id,
      rawId: made.rawId,
      type: 'public-key',
      response: PasskeyAttestationResponse(
        clientDataJSON: made.clientDataJSON,
        attestationObject: made.attestationObject,
        transports: made.transports.whereType<String>().toList(),
      ),
    );
  }

  @override
  Future<PasskeyAssertionCredential> get(
    PasskeyRequestOptions options, {
    bool autofill = false,
  }) async {
    final used = await _authenticator.authenticate(
      AuthenticateRequestType.fromJson(
        _withTransports(options.toJson(), 'allowCredentials'),
        mediation: autofill
            ? MediationType.Conditional
            : MediationType.Optional,
      ),
    );
    return PasskeyAssertionCredential(
      id: used.id,
      rawId: used.rawId,
      type: 'public-key',
      response: PasskeyAssertionResponse(
        clientDataJSON: used.clientDataJSON,
        authenticatorData: used.authenticatorData,
        signature: used.signature,
        userHandle: used.userHandle.isEmpty ? null : used.userHandle,
      ),
    );
  }
}

/// The options' JSON with `transports` on every credential descriptor in
/// [key]. Orvano leaves the field out when the server stored none for a
/// passkey (it is optional in the contract), and the `passkeys` package's
/// parser needs a list, so a missing one becomes empty.
Map<String, dynamic> _withTransports(Map<String, dynamic> json, String key) {
  final credentials = json[key];
  if (credentials is! List<dynamic>) return json;
  return {
    ...json,
    key: [
      for (final credential in credentials)
        if (credential is Map<String, dynamic>)
          {...credential, 'transports': credential['transports'] ?? <String>[]}
        else
          credential,
    ],
  };
}
