# Orvano Dart server quickstart

The finished app of the [Dart server quickstart](https://orvano.dev/docs/quickstarts/dart/): a `shelf` HTTP server on port 3001 whose `GET /me` checks a user's access token with `orvano_dart` and answers with the user, plus a command that creates a user with your API key.

To run it, start Orvano locally ([Run Orvano locally](https://orvano.dev/docs/local/)), create a server API key with `users.read` and `users.write`, then:

```bash
export ORVANO_ENDPOINT=http://localhost:7700
export ORVANO_PROJECT=your-project-id
export ORVANO_API_KEY=your-api-key
dart pub get
dart run bin/create_user.dart ada@example.com 'a long password' 'Ada Lovelace'
dart run bin/server.dart
```

Then sign in to get a token and call `GET http://localhost:3001/me` with it, as the quickstart shows.
