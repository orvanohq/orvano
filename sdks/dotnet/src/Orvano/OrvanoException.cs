namespace Orvano;

/// <summary>
/// The one exception every Orvano call throws when the server answers with a failure. The server
/// sends RFC 9457 problem details; <see cref="Code"/> is Orvano's stable error code (for example
/// <c>user_already_exists</c>).
/// </summary>
public sealed class OrvanoException : Exception
{
    /// <summary>Creates an <see cref="OrvanoException"/>.</summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="code">Orvano's stable error code.</param>
    /// <param name="message">A human readable description of the problem.</param>
    /// <param name="requestId">The request ID, when the server sent one.</param>
    public OrvanoException(int status, string code, string message, string? requestId)
        : base(message)
    {
        Status = status;
        Code = code;
        RequestId = requestId;
    }

    /// <summary>The HTTP status code.</summary>
    public int Status { get; }

    /// <summary>Orvano's stable error code, or <c>unknown</c> when the response carried none.</summary>
    public string Code { get; }

    /// <summary>The request ID to quote when reporting a problem, when the server sent one.</summary>
    public string? RequestId { get; }
}
