using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

using Orvano.SharedKernel.Outbox;

namespace Orvano.Infrastructure.Outbox;

/// <summary>
/// One outbox's statements, built once from its registered schema (spec 0013 DSP-8). The schema is the one identifier
/// in them that is not fixed text: <c>AddOutbox</c> checked it against <c>^[a-z][a-z0-9_]{1,30}\z</c> (OBX-3), it is
/// checked again here, and it is quoted, so it can never close the quote or carry SQL. Every value stays a parameter,
/// and every time comes from the injected clock (OTS-1).
/// </summary>
internal sealed class OutboxSql
{
    /// <summary>Rows per cleanup statement (OCL-1).</summary>
    public const int CleanupChunk = 5000;

    private readonly string _select;
    private readonly string _markDispatched;
    private readonly string _cleanup;

    public OutboxSql(string schema)
    {
        if (!OutboxServiceCollectionExtensions.IsValidSchema(schema))
        {
            throw new ArgumentException($"The outbox schema '{schema}' is not valid (spec 0013 DSP-8).", nameof(schema));
        }

        Schema = schema;
        string table = $"\"{schema}\".outbox"; // the table HasOutbox() maps (OTB-2)

        // DSP-3 (1): the waiting rows through ix_outbox_pending, skipping rows another dispatcher holds and rows of
        // transactions not yet committed. EF selects from it as a derived table, which PostgreSQL allows with the lock.
        _select = "SELECT id, type, payload::text AS payload, project_id, occurred_at, trace_parent FROM " + table
            + " WHERE dispatched_at IS NULL ORDER BY id LIMIT {0} FOR UPDATE SKIP LOCKED";

        // DSP-3 (3): every selected row, with or without subscribers.
        _markDispatched = "UPDATE " + table + " SET dispatched_at = {0} WHERE id = ANY({1})";

        // OCL-1, OCL-2: through the primary key, only rows that were dispatched.
        _cleanup = "DELETE FROM " + table + " WHERE id IN (SELECT id FROM " + table
            + " WHERE id < {0} AND dispatched_at IS NOT NULL ORDER BY id LIMIT " + CleanupChunk.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    public string Schema { get; }

    public FormattableString Select(int size) => FormattableStringFactory.Create(_select, size);

    public FormattableString MarkDispatched(DateTimeOffset now, Guid[] ids) => FormattableStringFactory.Create(_markDispatched, now, ids);

    public FormattableString Cleanup(Guid boundary) => FormattableStringFactory.Create(_cleanup, boundary);
}

/// <summary>One waiting outbox row, as DSP-3's select reads it.</summary>
internal sealed class OutboxRow
{
    [Column("id")]
    public Guid Id { get; init; }

    [Column("type")]
    public string Type { get; init; } = string.Empty;

    [Column("payload")]
    public string Payload { get; init; } = "{}";

    [Column("project_id")]
    public Guid? ProjectId { get; init; }

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; }

    [Column("trace_parent")]
    public string? TraceParent { get; init; }
}
