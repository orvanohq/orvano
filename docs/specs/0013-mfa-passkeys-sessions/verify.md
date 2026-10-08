# 0013. MFA and passkeys: manual checks on real devices and authenticator apps

The shared scenarios use a software authenticator (AC-45) and compute TOTP codes in the runners, so they can't prove that real authenticator apps, password managers, browsers, and phones behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches `Orvano.Auth`'s MFA or passkey code or the passkey helpers in the SDKs. Record the date, the Orvano version, and any deviation at the bottom.

## Setup

1. Deploy the branch to the test server, so `ORVANO_PUBLIC_URL` is its real https URL (this also turns on console passkeys, AC-3).
2. Host the sample Next.js app on a real domain you control, and register it as a web platform of a test project.
3. On the test project's Sign in methods page, turn on Passkeys with that domain as the RP ID, and add the sample Flutter app's Android signing certificate fingerprints (debug and release).
4. On that domain, serve `/.well-known/apple-app-site-association` (with `webcredentials` for the sample app's team and bundle ID) and `/.well-known/assetlinks.json` (with `delegate_permission/common.get_login_creds` for the sample app's package and fingerprints), copied from the card's snippets. Add the Associated Domains entitlement `webcredentials:<domain>` to the iOS app.
5. Give the test project and the install working SMTP, so the alert emails arrive.

## Checks

| # | Check | Expect | Covers |
|---|---|---|---|
| 1 | Enroll TOTP by scanning the QR code with Google Authenticator, then with 1Password, then with Microsoft Authenticator (turn off and on between) | Each app shows the project name and the email, and its first code confirms | AC-12, AC-13 |
| 2 | Sign in with the password, then the code from item 1 | Signed in; the session list shows level 2 and password plus authenticator | AC-6, AC-8, AC-26 |
| 3 | Sign in with a magic link and with Google (redirect) as the same user | Both stop at the MFA step | AC-6 |
| 4 | Safari on macOS and iOS with iCloud Keychain: add a passkey, sign out, sign in through the email field's autofill | The passkey is named "iCloud Keychain" and marked synced; sign in needs no typing | AC-20 to AC-24, AC-36 |
| 5 | Chrome on Windows and Android with Google Password Manager: the same as item 4 | Named "Google Password Manager"; autofill works | AC-20 to AC-24 |
| 6 | Firefox on Linux with a YubiKey: add the key as a passkey, then use it as the second step after a password | Marked device bound; the counter rises with each use | AC-11, AC-22 |
| 7 | Flutter on an iOS device: register and sign in with a passkey through the `passkeys` package | Works without a web view; the origin is `https://<rpId>` | AC-4, AC-38 |
| 8 | Flutter on an Android device (debug build, then release build): the same as item 7 | Works for both fingerprints; a build signed with an unlisted key fails | AC-4, AC-38 |
| 9 | Next.js sample: password sign in for an MFA user, then a passkey at the MFA step | `orvano_mfa` is `HttpOnly` in the browser's cookie view and gone after step two | AC-37 |
| 10 | Console: turn on TOTP and add a passkey for a console account on the Security page, sign out, sign in with each | Both work; the QR code scans; the step up dialog appears when removing the passkey | AC-41, AC-42 |
| 11 | Console: change the test project's RP ID to another domain, then back | The warning names the passkey count; passkeys stop, then work again | AC-2, AC-43 |
| 12 | Run `docker compose exec api orvano mfa reset --email <admin>` on the test server | Prints `reset`; the admin signs in with the password alone and gets the alert email | AC-28, AC-31 |
| 13 | Check the inbox of the test user after items 1, 4, 10, and 12 | One readable alert email per change, in a mail client and with a screen reader | AC-31 |

## Results

| Date | Version | Who | Deviations |
|---|---|---|---|
