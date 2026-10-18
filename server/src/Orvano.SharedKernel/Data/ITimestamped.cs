namespace Orvano.SharedKernel.Data;

/// <summary>
/// An editable entity with <c>created_at</c> and <c>updated_at</c>, both set by <see cref="TimestampInterceptor"/>
/// from the injected <see cref="TimeProvider"/> (spec 0011 COL-1). Any value a caller sets is overwritten on save.
/// </summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
