using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Jobs;
using Orvano.SharedKernel.Outbox;

namespace Orvano.Testing;

/// <summary>
/// The bad outbox registrations a CLI test can load into the real process through the startup hook, so it sees what
/// <c>serve</c> prints for each (spec 0013 OBX-7).
/// </summary>
public static class BadOutboxRegistrations
{
    public const string SchemaTwice = "schema-twice";
    public const string TypeInTwoOutboxes = "type-in-two-outboxes";
    public const string UnregisteredType = "unregistered-type";
    public const string OtherClrType = "other-clr-type";
    public const string NamedLikeAKind = "named-like-a-kind";

    /// <summary>The subscriber name every registration but <see cref="NamedLikeAKind"/> uses.</summary>
    public const string SubscriberName = "hook.subscriber";

    internal static void Add(IServiceCollection services, string which)
    {
        switch (which)
        {
            case SchemaTwice:
                services.AddSampleOutbox();
                services.AddOutbox(SampleOutboxDbContext.Schema, _ => { });
                break;
            case TypeInTwoOutboxes:
                services.AddSampleOutbox();
                services.AddSampleOutbox();
                break;
            case UnregisteredType:
                services.AddEventHandler<SampleNoteAdded, HookEventHandler>(SubscriberName);
                break;
            case OtherClrType:
                services.AddSampleOutbox();
                services.AddEventHandler<LookalikeNoteAdded, LookalikeHandler>(SubscriberName);
                break;
            default:
                services.AddSampleOutbox();
                services.AddJobHandler<HookJob, HookHandler>(HookJsonContext.Default.HookJob);
                services.AddEventHandler<SampleNoteAdded, HookEventHandler>(HookJob.Kind);
                break;
        }
    }
}

/// <summary>A record that claims <see cref="SampleNoteAdded"/>'s type with another CLR type.</summary>
internal sealed record LookalikeNoteAdded(Guid NoteId) : IOutboxEvent
{
    public static string Type => SampleNoteAdded.Type;
}

internal sealed class HookEventHandler : IEventHandler<SampleNoteAdded>
{
    public Task HandleAsync(SampleNoteAdded @event, EventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class LookalikeHandler : IEventHandler<LookalikeNoteAdded>
{
    public Task HandleAsync(LookalikeNoteAdded @event, EventContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(LookalikeNoteAdded))]
internal sealed partial class HookOutboxJsonContext : JsonSerializerContext;
