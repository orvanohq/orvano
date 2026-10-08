import 'package:orvano_core/orvano_core.dart';
import 'package:passkeys/authenticator.dart' as platform;
import 'package:passkeys/types.dart';

/// The default [PasskeyAuthenticator] of a Flutter client (spec 0013, AC-38):
/// the `passkeys` package, which uses `ASAuthorization` on iOS and macOS,
/// Credential Manager on Android, and WebAuthn on the web.
///
/// The app sets up its side once: an Associated Domains entry
/// `webcredentials:<rpId>` on iOS and macOS, and on Android a
/// `/.well-known/assetlinks.json` on the RP ID's site that names the app's
/// signing certificate (the project's Passkeys card gives both snippets). On
/// the web, `passkeys` needs its script in `web/index.html`; see its README.
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
      RegisterRequestType.fromJson(options.toJson()),
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
        options.toJson(),
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
