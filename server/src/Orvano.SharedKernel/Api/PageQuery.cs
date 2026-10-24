using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Mvc;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// The paging inputs every list operation takes (spec 0014 PAG-1), bound with <c>[AsParameters]</c> beside
/// <c>.WithPaging()</c>. An out of range <c>limit</c> fails validation with <c>out_of_range</c>.
/// </summary>
public sealed record PageQuery
{
    /// <summary>The page size when the caller sends none, the spec 0001 default.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page a caller may ask for.</summary>
    public const int MaxLimit = 100;

    /// <summary>The longest cursor the server reads.</summary>
    public const int MaxCursorLength = 2048;

    /// <summary>How many rows to return, 1 to 100; 50 when not sent.</summary>
    [FromQuery(Name = "limit")]
    [Range(1, MaxLimit, ErrorMessage = FieldCodes.OutOfRange)]
    public int? Limit { get; init; }

    /// <summary>The <c>nextCursor</c> of the previous page; omit it for the first page.</summary>
    [FromQuery(Name = "cursor")]
    [MaxLength(MaxCursorLength, ErrorMessage = FieldCodes.TooLong)]
    public string? Cursor { get; init; }

    /// <summary>The page size to use: <see cref="Limit"/>, or <see cref="DefaultLimit"/>.</summary>
    public int Size => Limit ?? DefaultLimit;
}
