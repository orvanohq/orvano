namespace Orvano.SharedKernel.Api;

/// <summary>
/// Marks an error code constant for the contract's catalogue (spec 0014 PRB-7, XTN-1): the status it answers with and
/// what it means. Module codes live in <c>&lt;Module&gt;ErrorCodes</c> in the module's <c>Contracts/</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ErrorCodeAttribute : Attribute
{
    public ErrorCodeAttribute(ErrorKind kind, string description)
    {
        Kind = kind;
        Status = ErrorProblems.StatusFor(kind);
        Description = description;
    }

    // The shared transport codes (405, 413, 415, 500) answer with statuses no ErrorKind has.
    internal ErrorCodeAttribute(int status, string description)
    {
        Status = status;
        Description = description;
    }

    /// <summary>The kind a handler builds the error with, or null for a transport code the framework answers.</summary>
    public ErrorKind? Kind { get; }

    public int Status { get; }

    public string Description { get; }
}
