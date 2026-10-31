import { t } from "@lingui/core/macro";

/** Spec 0017 PSW-1: what a new password's refusal says, by its field code. */
export function passwordFailure(code: string): string {
  switch (code) {
    case "password_too_common":
      return t`This password is too common. Choose another.`;
    case "too_long":
      return t`This password is too long.`;
    default:
      return t`Use at least 8 characters.`;
  }
}
