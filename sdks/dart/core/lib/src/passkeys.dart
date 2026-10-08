import 'client.dart';
import 'generated/models.dart';
import 'generated/services.dart';

/// Makes and uses passkeys for a client (spec 0013, AC-38): `create` answers
/// `navigator.credentials.create` and `get` answers `navigator.credentials.get`,
/// both in WebAuthn's JSON form. `orvano_flutter` gives every client a default
/// one built on the `passkeys` package (iOS, Android, and the web); a Dart app
/// or a test passes its own.
abstract interface class PasskeyAuthenticator {
  /// Makes a new passkey for [options] and answers it.
  Future<PasskeyRegistrationCredential> create(PasskeyCreationOptions options);

  /// Signs [options]' challenge with a passkey the user picks. With
  /// [autofill], the platform offers the passkeys through autofill
  /// (conditional mediation) instead of a sheet, where it can.
  Future<PasskeyAssertionCredential> get(
    PasskeyRequestOptions options, {
    bool autofill = false,
  });
}

final _authenticators = Expando<PasskeyAuthenticator>(
  'orvano.passkeys.authenticator',
);

/// Gives [client] the authenticator its passkey helpers use when none is
/// passed. `orvano_flutter`'s `createClient` sets one built on the `passkeys`
/// package.
void setDefaultPasskeyAuthenticator(
  Client client,
  PasskeyAuthenticator authenticator,
) {
  _authenticators[client] = authenticator;
}

/// [given], else [client]'s default authenticator. Throws an [ArgumentError]
/// when there is neither.
PasskeyAuthenticator passkeyAuthenticatorFor(
  Client client,
  PasskeyAuthenticator? given,
) =>
    given ??
    _authenticators[client] ??
    (throw ArgumentError.value(
      null,
      'authenticator',
      'Pass a PasskeyAuthenticator, or set one with '
          'setDefaultPasskeyAuthenticator (orvano_flutter does).',
    ));

/// Passkey sign in and registration (spec 0013, AC-38).
extension PasskeySignIn on Client {
  /// Signs in with a passkey (`account.createPasskeyChallenge`, the
  /// authenticator, then `account.createPasskeySession`), stores the session,
  /// emits `AuthEvent.signedIn`, and returns the user. A passkey counts as two
  /// factors, so this sign in never stops at the MFA step. With [autofill],
  /// the passkeys are offered through autofill where the platform can.
  ///
  /// Throws an [ArgumentError], before any call, when there is no
  /// authenticator, and an `OrvanoException` for a refused answer
  /// (`invalid_passkey`) or passkeys turned off (`factor_not_enabled`).
  Future<User> signInWithPasskey({
    PasskeyAuthenticator? authenticator,
    bool autofill = false,
    RequestOptions? options,
  }) async {
    final passkeys = passkeyAuthenticatorFor(this, authenticator);
    final account = AccountService(this);
    final challenge = await account.createPasskeyChallenge(options: options);
    final credential = await passkeys.get(
      challenge.options,
      autofill: autofill,
    );
    final result = await account.createPasskeySession(
      CreatePasskeySessionRequest(
        challengeId: challenge.challengeId,
        credential: credential,
      ),
      options: options,
    );
    return result.user!;
  }

  /// Adds a passkey to the signed in user (`account.createPasskeyRegistration`,
  /// the authenticator, then `account.completePasskeyRegistration`), named
  /// [name] (1 to 64 characters; `Passkey` when left out). Needs a session
  /// that signed in, or passed a second factor, within 10 minutes and, when
  /// the user has an email, a verified one.
  Future<Passkey> registerPasskey({
    String? name,
    PasskeyAuthenticator? authenticator,
    RequestOptions? options,
  }) async {
    final passkeys = passkeyAuthenticatorFor(this, authenticator);
    final account = AccountService(this);
    final registration = await account.createPasskeyRegistration(
      options: options,
    );
    final credential = await passkeys.create(registration.options);
    return account.completePasskeyRegistration(
      CompletePasskeyRegistrationRequest(
        challengeId: registration.challengeId,
        credential: credential,
        name: name,
      ),
      options: options,
    );
  }
}
