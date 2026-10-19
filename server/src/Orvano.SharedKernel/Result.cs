using System.Diagnostics.CodeAnalysis;

namespace Orvano.SharedKernel;

/// <summary>
/// A value or an expected failure (spec 0002 ERR-1, ERR-2). <c>default(Result&lt;T&gt;)</c> is never a success.
/// </summary>
public readonly struct Result<T>
{
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _isSuccess = true;
        Value = value;
    }

    private Result(Error error) => Error = error;

    public T? Value { get; }

    public Error? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess && Error is null;

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>A command's outcome with no value (spec 0002 ERR-1).</summary>
public readonly struct Result
{
    private readonly bool _isSuccess;

    private Result(bool isSuccess, Error? error)
    {
        _isSuccess = isSuccess;
        Error = error;
    }

    public static Result Success { get; } = new(true, null);

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess && Error is null;

    public static implicit operator Result(Error error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}
