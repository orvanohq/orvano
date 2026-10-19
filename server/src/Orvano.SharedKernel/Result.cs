using System.Diagnostics.CodeAnalysis;

namespace Orvano.SharedKernel;

/// <summary>
/// A value or an expected failure (spec 0002 ERR-1, ERR-2). <c>default(Result&lt;T&gt;)</c> is never a success: its
/// <see cref="Error"/> is <see cref="DefaultResult.Error"/>.
/// </summary>
public readonly struct Result<T>
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(T value)
    {
        _isSuccess = true;
        Value = value;
    }

    private Result(Error error) => _error = error;

    public T? Value { get; }

    public Error? Error => _isSuccess ? null : _error ?? DefaultResult.Error;

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>
/// A command's outcome with no value (spec 0002 ERR-1). <c>default(Result)</c> is never a success: its
/// <see cref="Error"/> is <see cref="DefaultResult.Error"/>.
/// </summary>
public readonly struct Result
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    public static Result Success { get; } = new(true, null);

    public Error? Error => _isSuccess ? null : _error ?? DefaultResult.Error;

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    public static implicit operator Result(Error error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>
/// The error a <c>default</c> <see cref="Result"/> or <see cref="Result{T}"/> carries, so a failed result always has
/// an error. Seeing it means a result was never set: a bug, answered as unavailable.
/// </summary>
internal static class DefaultResult
{
    public static Error Error { get; } = Error.Unavailable("default_result", "The operation did not produce a result.");
}
