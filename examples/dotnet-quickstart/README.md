# Orvano .NET quickstart

The finished app of the [.NET quickstart](https://orvano.dev/docs/quickstarts/dotnet/): an ASP.NET Core Minimal API on port 3002 whose `GET /me` checks a user's access token with the `Orvano` package and answers with the user, plus a `create-user` command that creates a user with your API key.

To run it, start Orvano locally ([Run Orvano locally](https://orvano.dev/docs/local/)), create a server API key with `users.read` and `users.write`, then:

```bash
export ORVANO_ENDPOINT=http://localhost:7700
export ORVANO_PROJECT=your-project-id
export ORVANO_API_KEY=your-api-key
dotnet run -- create-user ada@example.com 'a long password' 'Ada Lovelace'
dotnet run
```

Then sign in to get a token and call `GET http://localhost:3002/me` with it, as the quickstart shows.
