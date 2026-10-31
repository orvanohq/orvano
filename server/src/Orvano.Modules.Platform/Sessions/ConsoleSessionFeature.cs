using Orvano.SharedKernel.Access;

namespace Orvano.Modules.Platform.Sessions;

internal sealed record ConsoleSessionFeature(
    Guid SessionId, ReadOnlyMemory<byte> CsrfToken, DateTimeOffset LastReauthAt, DateTimeOffset ExpiresAt) : IConsoleSessionFeature;
