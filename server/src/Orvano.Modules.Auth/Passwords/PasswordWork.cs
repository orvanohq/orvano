using System.Security.Cryptography;

using Microsoft.Extensions.Options;

using Orvano.Modules.Auth.Contracts;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Api;

namespace Orvano.Modules.Auth.Passwords;

/// <summary>
/// Wraps <see cref="IPasswordHasher"/> with PWD-4's concurrency limit (spec 0017 PSW-4). Hashing takes a thread for its
/// whole run, so it leaves the request thread, and the limit keeps a flood of sign ins from using all memory.
/// </summary>
internal sealed class PasswordWork : IPasswordWork, IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly Error Busy =
        Error.Unavailable(ErrorCodes.Unavailable, "The server is busy; wait a moment and try again.", Wait);

    private readonly IPasswordHasher _hasher;
    private readonly SemaphoreSlim _slots;
    private readonly Argon2Cost _cost;
    private readonly string _dummy;

    public PasswordWork(IPasswordHasher hasher, IOptions<AuthOptions> options)
        : this(hasher, options.Value.HashConcurrency ?? DefaultConcurrency(Argon2Cost.Default), Argon2Cost.Default)
    {
    }

    internal PasswordWork(IPasswordHasher hasher, int concurrency, Argon2Cost cost)
    {
        _hasher = hasher;
        _slots = new SemaphoreSlim(concurrency, concurrency);
        _cost = cost;

        // PSW-5: a PHC string with the current parameters and random bytes, which no password matches. Only the derive
        // costs anything, so making it here costs nothing.
        _dummy = Argon2idPasswordHasher.Format(cost, RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(32));
    }

    public async Task<Result<string>> HashAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!await _slots.WaitAsync(Wait, cancellationToken))
        {
            return Busy;
        }

        try
        {
            return await Task.Run(() => _hasher.Hash(password), CancellationToken.None);
        }
        finally
        {
            _slots.Release();
        }
    }

    public async Task<Result<bool>> VerifyAsync(string password, string? hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!await _slots.WaitAsync(Wait, cancellationToken))
        {
            return Busy;
        }

        try
        {
            string stored = hash ?? _dummy;
            bool matches = await Task.Run(() => _hasher.Verify(password, stored), CancellationToken.None);
            return matches && hash is not null;
        }
        finally
        {
            _slots.Release();
        }
    }

    public bool NeedsRehash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return Argon2idPasswordHasher.TryReadCost(hash, out Argon2Cost stored)
            && (stored.MemoryKiB < _cost.MemoryKiB || stored.Iterations < _cost.Iterations);
    }

    public void Dispose() => _slots.Dispose();

    // PWD-4: the larger of 2 and the memory budget over the Argon2 cost. The budget is a quarter of what the process may
    // use, which honors a container's limit.
    private static int DefaultConcurrency(Argon2Cost cost)
    {
        long budgetKiB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4 / 1024;
        return (int)Math.Clamp(budgetKiB / cost.MemoryKiB, 2, 1024);
    }
}
