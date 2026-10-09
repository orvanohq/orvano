# Changelog

Every Orvano SDK shares the server's version. The notes for each release are on the [Orvano releases page](https://github.com/orvanohq/orvano/releases).

## Unreleased

- Adding a second factor or a provider needs the user's current password (spec 0013): `account.createTotp`, `account.createPasskeyRegistration`, `account.createOAuthLinkFlow`, and `account.createIdTokenIdentity` take a body with an optional `password` (`CreateTotpRequest`, `CreatePasskeyRegistrationRequest`, `CreateOAuthLinkFlowRequest`, `CreateIdTokenIdentityRequest`). A user with a password must send it unless the session passed a second factor within 10 minutes; a missing or wrong one is refused with `invalid_credentials`. `registerPasskey`, `linkIdentity`, and `linkIdentityWithIdToken` take `password` and send it.
- `account.verifyMfa` answers a `RaisedSession` (`accessToken`, `accessTokenExpiresAt`, `sessionId`), and so does `TotpConfirmation.session`: no refresh token comes back. `Client.verifyMfa` and `Client.confirmTotp` store the new access token and keep the refresh token the session store holds.

## 0.2.0

- See the [release notes](https://github.com/orvanohq/orvano/releases/tag/v0.2.0).

## 0.0.0

- First preview: the client, errors, pagination, events, retries, and the health check.
