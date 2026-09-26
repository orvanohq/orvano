# Orvano for .NET

The Orvano SDK for .NET server code (`net10.0` and `netstandard2.0`).

## Use it

```bash
dotnet add package Orvano
```

```csharp
using Orvano;

using var orvano = new OrvanoClient(new OrvanoClientOptions(new Uri("https://orvano.example.com"))
{
    ApiKey = Environment.GetEnvironmentVariable("ORVANO_API_KEY"),
});
var health = await orvano.Health.GetAsync();
```

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview and only covers the health check so far.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
