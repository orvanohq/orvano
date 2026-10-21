using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orvano.SharedKernel.Data;
using Orvano.SharedKernel.Outbox;

namespace Orvano.Testing;

/// <summary>
/// A test only platform context with an outbox (spec 0013 OTS-3), so the outbox path is proven before any module owns
/// one. Not in <c>MigrationOrder</c>: <see cref="CreateTablesAsync(IServiceProvider, CancellationToken)"/> creates its schema from the model.
/// </summary>
public sealed class SampleOutboxDbContext(DbContextOptions<SampleOutboxDbContext> options) : DbContext(options)
{
    public const string Schema = "sample";

    public DbSet<SampleNote> Notes => Set<SampleNote>();

    /// <summary>The context in <paramref name="schema"/>, <c>sample</c> by default; another schema gives a second outbox.</summary>
    public static SampleOutboxDbContext On(IServiceProvider host, string schema = Schema) =>
        new(new DbContextOptionsBuilder<SampleOutboxDbContext>().UseOrvanoConventions(schema, host).Options);

    /// <summary>
    /// Creates the schema and tables on <paramref name="host"/>'s platform database by running the context's create
    /// script (<c>EnsureCreated</c> does nothing on a database that already has tables).
    /// </summary>
    public static Task CreateTablesAsync(IServiceProvider host, CancellationToken cancellationToken) =>
        CreateTablesAsync(host, Schema, cancellationToken);

    /// <inheritdoc cref="CreateTablesAsync(IServiceProvider, CancellationToken)"/>
    public static async Task CreateTablesAsync(IServiceProvider host, string schema, CancellationToken cancellationToken)
    {
        string script;
        await using (SampleOutboxDbContext db = On(host, schema))
        {
            script = db.Database.GenerateCreateScript();
        }

        await using NpgsqlCommand command = host.GetRequiredService<NpgsqlDataSource>().CreateCommand(script);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<SampleNote>(note => note.ToTable("note"));
        modelBuilder.HasOutbox();
    }
}

/// <summary>The sample context's own row, saved in the same transaction as an event.</summary>
public sealed class SampleNote : Entity
{
    [DataClass(DataClass.Public)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>A sample event with a note id and a free text field, for size and content checks.</summary>
public sealed record SampleNoteAdded(Guid NoteId, string Text = "") : IOutboxEvent
{
    public static string Type => "sample.note.added.v1";
}

/// <summary>A second sample event, which a <see cref="SampleNoteAdded"/> subscriber can publish to form a chain.</summary>
public sealed record SampleNoteChained(Guid NoteId) : IOutboxEvent
{
    public static string Type => "sample.note.chained.v1";
}

/// <summary>An event of another module's schema, which the sample context must refuse to publish (PUB-2).</summary>
public sealed record OtherModuleEvent(Guid Id) : IOutboxEvent
{
    public static string Type => "other.thing.happened.v1";
}

/// <summary>An event no outbox registers.</summary>
public sealed record UnregisteredSampleEvent(Guid Id) : IOutboxEvent
{
    public static string Type => "sample.not.registered.v1";
}

[JsonSerializable(typeof(SampleNoteAdded))]
[JsonSerializable(typeof(SampleNoteChained))]
[JsonSerializable(typeof(OtherModuleEvent))]
[JsonSerializable(typeof(UnregisteredSampleEvent))]
public sealed partial class SampleOutboxJsonContext : JsonSerializerContext;

public static class SampleOutbox
{
    /// <summary>Registers the <c>sample</c> outbox with <see cref="SampleNoteAdded"/> and <see cref="SampleNoteChained"/> (OBX-3).</summary>
    public static IServiceCollection AddSampleOutbox(this IServiceCollection services) =>
        services.AddOutbox(SampleOutboxDbContext.Schema, outbox => outbox
            .Event(SampleOutboxJsonContext.Default.SampleNoteAdded)
            .Event(SampleOutboxJsonContext.Default.SampleNoteChained));
}
