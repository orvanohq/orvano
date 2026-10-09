import 'client.dart';
import 'generated/models.dart';
import 'generated/services.dart';
import 'oauth.dart' show OAuthSignIn;

/// What an emailed link is for: its `orvano_type` parameter (spec 0010). The
/// link also carries the secret as `orvano_token`.
enum EmailLinkType {
  /// Verifies the email (`account.verifyEmail`).
  verification('verification'),

  /// Resets the password and signs in (`account.completeRecovery`).
  recovery('recovery'),

  /// Signs in (`account.createMagicLinkSession`).
  magicLink('magic_link'),

  /// Confirms an email change (`account.confirmEmailChange`).
  emailChange('email_change');

  const EmailLinkType(this.wire);

  /// The `orvano_type` value.
  final String wire;
}

/// The query parameter Orvano sets to the link's [EmailLinkType].
const linkTypeParameter = 'orvano_type';

/// The query parameter Orvano sets to the link's single use token.
const linkTokenParameter = 'orvano_token';

/// An emailed link, read from its URL by [readEmailLink].
final class EmailLink {
  /// Creates a link.
  const EmailLink({required this.type, required this.token, this.password});

  /// What the link is for.
  final EmailLinkType type;

  /// The `orvano_token` parameter; it works once.
  final String token;

  /// The new password, for a [EmailLinkType.recovery] link only.
  final String? password;
}

/// A link `handleLink` finished: an emailed link, a provider sign in, or a
/// provider link.
sealed class HandledLink {
  const HandledLink();

  /// True only when the call created the user.
  bool get isNewUser;

  /// True when the sign in stopped at the MFA step (spec 0013): the user has
  /// MFA on, so no session exists yet.
  bool get mfaRequired => false;

  /// The factors the MFA challenge offers; empty when [mfaRequired] is false.
  List<MfaFactor> get factors => const [];
}

/// A finished provider sign in: the user, and whether it created them, or
/// the MFA step it stopped at ([mfaRequired]).
final class OAuthSignInResult extends HandledLink {
  /// Creates a result.
  const OAuthSignInResult({
    required this.user,
    required this.isNewUser,
    this.mfaRequired = false,
    this.factors = const [],
  });

  /// The result of a sign in's [AuthResult].
  factory OAuthSignInResult.of(AuthResult result) => OAuthSignInResult(
    user: result.user,
    isNewUser: result.isNewUser,
    mfaRequired: result.mfa != null,
    factors: result.mfa?.factors ?? const [],
  );

  /// The signed in user; null while [mfaRequired] is true.
  final User? user;

  @override
  final bool isNewUser;

  @override
  final bool mfaRequired;

  @override
  final List<MfaFactor> factors;
}

/// A finished link of a provider to the signed in user.
final class IdentityLinkResult extends HandledLink {
  /// Creates a result.
  const IdentityLinkResult({required this.identity});

  /// The new identity.
  final Identity identity;

  @override
  bool get isNewUser => false;
}

/// What a redeemed link did: its type, the user, and whether the call created
/// them. A magic link or reset for a user with MFA on stops at the MFA step:
/// [mfaRequired] is true and [user] null.
final class LinkResult extends HandledLink {
  /// Creates a result.
  const LinkResult({
    required this.type,
    required this.user,
    required this.isNewUser,
    this.mfaRequired = false,
    this.factors = const [],
  });

  /// The result of a link that signs in, from its [AuthResult].
  factory LinkResult.of(EmailLinkType type, AuthResult result) => LinkResult(
    type: type,
    user: result.user,
    isNewUser: result.isNewUser,
    mfaRequired: result.mfa != null,
    factors: result.mfa?.factors ?? const [],
  );

  /// What the link was for.
  final EmailLinkType type;

  /// The user, as it is now; null while [mfaRequired] is true.
  final User? user;

  /// True only when a magic link created the user.
  @override
  final bool isNewUser;

  @override
  final bool mfaRequired;

  @override
  final List<MfaFactor> factors;
}

/// Reads an emailed link from [uri]: null when it carries neither
/// `orvano_type` nor `orvano_token`. Throws an [ArgumentError], before any
/// call, for an unknown type, a missing token, or a recovery link without a
/// [password].
EmailLink? readEmailLink(Uri uri, {String? password}) {
  final type = uri.queryParameters[linkTypeParameter];
  final token = uri.queryParameters[linkTokenParameter];
  if (type == null && token == null) return null;
  final kind = EmailLinkType.values.where((t) => t.wire == type).firstOrNull;
  if (kind == null) {
    throw ArgumentError.value(
      type,
      linkTypeParameter,
      'is not a known link type',
    );
  }
  if (token == null || token.isEmpty) {
    throw ArgumentError.value(token, linkTokenParameter, 'is missing');
  }
  if (kind == EmailLinkType.recovery) {
    if (password == null || password.isEmpty) {
      throw ArgumentError.value(
        password,
        'password',
        'is required for a recovery link',
      );
    }
    return EmailLink(type: kind, token: token, password: password);
  }
  return EmailLink(type: kind, token: token);
}

/// The link helper (spec 0010, AC-24).
extension EmailLinks on Client {
  /// Redeems a link Orvano emailed: reads `orvano_type` and `orvano_token`
  /// from [uri], calls the matching operation, and returns what it did, or
  /// null when [uri] carries neither parameter. A magic link or password reset
  /// stores the new session (replacing any) and emits
  /// [AuthEvent.signedIn]; a verification or email change refreshes and emits
  /// [AuthEvent.userUpdated] when this client holds that user's session.
  ///
  /// It also finishes a provider redirect (spec 0012): `orvano_type`
  /// `oauth` signs in ([OAuthSignInResult]) and `oauth_link` links the
  /// provider ([IdentityLinkResult]), with the verifier `signInWithOAuth` or
  /// `linkIdentity` kept; an `orvano_error` throws an [OrvanoException] with
  /// that code.
  ///
  /// Wire your app's deep links (`app_links`, `go_router`) to pass the [Uri].
  /// Throws an [ArgumentError], before any call, for an unknown type or a
  /// recovery link without [password], and an [OrvanoException] when Orvano
  /// refuses the link (for example `invalid_email_token`).
  Future<HandledLink?> handleLink(
    Uri uri, {
    String? password,
    RequestOptions? options,
  }) async {
    final redirect = await handleOAuthRedirect(uri, options: options);
    if (redirect != null) return redirect;
    final link = readEmailLink(uri, password: password);
    if (link == null) return null;
    final account = AccountService(this);
    switch (link.type) {
      case EmailLinkType.magicLink:
        final result = await account.createMagicLinkSession(
          CreateMagicLinkSessionRequest(token: link.token),
          options: options,
        );
        return LinkResult.of(link.type, result);
      case EmailLinkType.recovery:
        final result = await account.completeRecovery(
          CompleteRecoveryRequest(token: link.token, password: link.password!),
          options: options,
        );
        return LinkResult.of(link.type, result);
      case EmailLinkType.verification:
        final user = await account.verifyEmail(
          VerifyEmailRequest(token: link.token),
          options: options,
        );
        return LinkResult(type: link.type, user: user, isNewUser: false);
      case EmailLinkType.emailChange:
        final user = await account.confirmEmailChange(
          ConfirmEmailChangeRequest(token: link.token),
          options: options,
        );
        return LinkResult(type: link.type, user: user, isNewUser: false);
    }
  }
}
