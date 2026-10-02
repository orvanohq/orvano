import 'client.dart';
import 'generated/models.dart';
import 'generated/services.dart';

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

/// What a redeemed link did: its type, the user, and whether the call created
/// them.
final class LinkResult {
  /// Creates a result.
  const LinkResult({
    required this.type,
    required this.user,
    required this.isNewUser,
  });

  /// What the link was for.
  final EmailLinkType type;

  /// The user, as it is now.
  final User user;

  /// True only when a magic link created the user.
  final bool isNewUser;
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
  /// Wire your app's deep links (`app_links`, `go_router`) to pass the [Uri].
  /// Throws an [ArgumentError], before any call, for an unknown type or a
  /// recovery link without [password], and an [OrvanoException] when Orvano
  /// refuses the link (for example `invalid_email_token`).
  Future<LinkResult?> handleLink(
    Uri uri, {
    String? password,
    RequestOptions? options,
  }) async {
    final link = readEmailLink(uri, password: password);
    if (link == null) return null;
    final account = AccountService(this);
    switch (link.type) {
      case EmailLinkType.magicLink:
        final result = await account.createMagicLinkSession(
          CreateMagicLinkSessionRequest(token: link.token),
          options: options,
        );
        return LinkResult(
          type: link.type,
          user: result.user,
          isNewUser: result.isNewUser,
        );
      case EmailLinkType.recovery:
        final result = await account.completeRecovery(
          CompleteRecoveryRequest(token: link.token, password: link.password!),
          options: options,
        );
        return LinkResult(
          type: link.type,
          user: result.user,
          isNewUser: result.isNewUser,
        );
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
