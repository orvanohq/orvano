# Changelog

Every Orvano SDK shares the server's version. The notes for each release are on the [Orvano releases page](https://github.com/orvanohq/orvano/releases).

## Unreleased

- From `orvano_core` (spec 0013): `registerPasskey`, `linkIdentity`, `linkIdentityWithIdToken`, and `account.createTotp` take the user's current `password`, and `verifyMfa` and `confirmTotp` store only a new access token, keeping the refresh token in secure storage. See the `orvano_core` changelog.

## 0.2.0

- See the [release notes](https://github.com/orvanohq/orvano/releases/tag/v0.2.0).

## 0.0.0

- First preview: the client, errors, pagination, events, retries, and the health check.
