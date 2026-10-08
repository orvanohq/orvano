import 'generated/models.dart';

/// A sign in that stopped at the MFA step (spec 0013, AC-38): the user has
/// MFA on, so no session exists yet. Finish it with `Client.completeMfa`
/// before [expiresAt]. The ticket stays inside the client's memory, so an app
/// restart starts over.
final class PendingMfa {
  /// Creates a pending sign in.
  const PendingMfa({required this.factors, required this.expiresAt});

  /// The factors the user can answer with now, in the challenge's order.
  final List<MfaFactor> factors;

  /// When the challenge stops working; after that, sign in again.
  final DateTime expiresAt;
}

/// One second factor for `Client.completeMfa` and `Client.verifyMfa`.
sealed class MfaAnswer {
  const MfaAnswer();

  /// The 6 digit code the authenticator app shows now.
  const factory MfaAnswer.totp(String code) = TotpAnswer;

  /// One of the user's recovery codes; case, spaces, and hyphens do not
  /// matter.
  const factory MfaAnswer.recoveryCode(String code) = RecoveryCodeAnswer;

  /// The answer's field in a request body.
  Map<String, String> toJson();
}

/// An authenticator app code.
final class TotpAnswer extends MfaAnswer {
  /// Creates the answer.
  const TotpAnswer(this.code);

  /// The 6 digit code.
  final String code;

  @override
  Map<String, String> toJson() => {'totpCode': code};
}

/// A recovery code.
final class RecoveryCodeAnswer extends MfaAnswer {
  /// Creates the answer.
  const RecoveryCodeAnswer(this.code);

  /// The recovery code, as the user typed it.
  final String code;

  @override
  Map<String, String> toJson() => {'recoveryCode': code};
}
