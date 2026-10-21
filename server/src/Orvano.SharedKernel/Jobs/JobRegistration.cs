using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Configuration;

namespace Orvano.SharedKernel.Jobs;

/// <summary>How the runner treats one kind of job (spec 0013 JQ-5).</summary>
public sealed class JobKindOptions
{
    public static readonly TimeSpan MinLease = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxLease = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinTimeout = TimeSpan.FromSeconds(1);
    public const int MaxMaxAttempts = 100;

    /// <summary>How long a claim holds the job before another worker may take it; renewed every third. 1 minute to 1 hour.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs in all, 1 to 100. 1 means at most one run only when nothing fails, since a lost claim acknowledgement can
    /// kill the job unrun (CLM-3).
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The longest one attempt may run before its token is cancelled and it counts as failed. At least 1 second.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// True when every job of the kind runs for a project: enqueue refuses one without a project id, and a row without
    /// one dies at once (ENQ-3, OUT-4).
    /// </summary>
    public bool RequiresProject { get; set; }
}

/// <summary>
/// One registered job kind (spec 0013 JQ-5): its type, its JSON type info from the module's source generated context
/// (spec 0002 JSN-1), its options, and delegates that read the payload and call the scoped handler, so the runner needs
/// no reflection. Made by <c>AddJobHandler</c>; read by <c>Orvano.Infrastructure</c>.
/// </summary>
public sealed class JobRegistration
{
    private readonly Func<string, object?> _deserialize;
    private readonly Func<object, string> _serialize;
    private readonly Func<IServiceProvider, object, JobContext, CancellationToken, Task> _run;

    internal JobRegistration(
        string kind,
        Type jobType,
        JobKindOptions options,
        Func<string, object?> deserialize,
        Func<object, string> serialize,
        Func<IServiceProvider, object, JobContext, CancellationToken, Task> run)
    {
        Kind = kind;
        JobType = jobType;
        Options = options;
        _deserialize = deserialize;
        _serialize = serialize;
        _run = run;
    }

    public string Kind { get; }

    public Type JobType { get; }

    public JobKindOptions Options { get; }

    /// <summary>The payload JSON of <paramref name="job"/>, an instance of <see cref="JobType"/>.</summary>
    public string Serialize(object job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _serialize(job);
    }

    /// <summary>Reads a payload. Throws <see cref="JsonException"/> when it does not deserialize, or deserializes to null.</summary>
    public object Deserialize(string payload) =>
        _deserialize(payload) ?? throw new JsonException($"The payload of a {Kind} job is null.");

    /// <summary>Resolves the kind's handler from <paramref name="scope"/> and runs it on <paramref name="job"/>.</summary>
    public Task RunAsync(IServiceProvider scope, object job, JobContext context, CancellationToken cancellationToken) =>
        _run(scope, job, context, cancellationToken);
}

/// <summary>One schedule declared in code (spec 0013 SCH-1, SCH-2). Made by <c>AddJobSchedule</c>; read by <c>Orvano.Infrastructure</c>.</summary>
public sealed class JobScheduleRegistration
{
    internal JobScheduleRegistration(string name, string cron, string kind, Type jobType, object job)
    {
        Name = name;
        Cron = cron;
        Kind = kind;
        JobType = jobType;
        Job = job;
    }

    /// <summary>The schedule's name, which also prefixes each fire's dedupe key.</summary>
    public string Name { get; }

    /// <summary>A five field <c>Cronos</c> expression, evaluated in UTC.</summary>
    public string Cron { get; }

    public string Kind { get; }

    public Type JobType { get; }

    /// <summary>The job each fire enqueues; serialized with its kind's <see cref="JobRegistration"/>.</summary>
    public object Job { get; }
}

/// <summary>Registers job kinds and schedules (spec 0013 JQ-5). Modules call both in <c>Add&lt;Module&gt;Module</c>, for every command.</summary>
public static partial class JobServiceCollectionExtensions
{
    private const int MinNameLength = 3;
    private const int MaxNameLength = 100;

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the scoped handler of <typeparamref name="TJob"/>'s kind, with the
    /// JSON type info from the module's source generated <c>JsonSerializerContext</c>.
    /// </summary>
    /// <exception cref="ConfigurationException">The kind is not a valid name, or an option is out of range.</exception>
    public static IServiceCollection AddJobHandler<TJob, THandler>(
        this IServiceCollection services,
        JsonTypeInfo<TJob> jsonTypeInfo,
        Action<JobKindOptions>? configure = null)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        string kind = TJob.Kind;
        EnsureValidName(kind, "job kind");
        var options = new JobKindOptions();
        configure?.Invoke(options);
        EnsureValidOptions(kind, options);

        services.AddScoped<IJobHandler<TJob>, THandler>();
        services.AddSingleton(new JobRegistration(
            kind,
            typeof(TJob),
            options,
            payload => JsonSerializer.Deserialize(payload, jsonTypeInfo),
            job => JsonSerializer.Serialize((TJob)job, jsonTypeInfo),
            (scope, job, context, cancellationToken) =>
                scope.GetRequiredService<IJobHandler<TJob>>().HandleAsync((TJob)job, context, cancellationToken)));
        return services;
    }

    /// <summary>
    /// Registers a schedule that enqueues <paramref name="job"/> at every occurrence of <paramref name="cron"/> (five
    /// fields, UTC), once across all workers. Startup checks the cron, the kind's handler, and the payload size.
    /// </summary>
    /// <exception cref="ConfigurationException">The name is not valid.</exception>
    public static IServiceCollection AddJobSchedule<TJob>(this IServiceCollection services, string name, string cron, TJob job)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cron);
        ArgumentNullException.ThrowIfNull(job);

        EnsureValidName(name, "job schedule name");
        services.AddSingleton(new JobScheduleRegistration(name, cron, TJob.Kind, typeof(TJob), job));
        return services;
    }

    /// <summary>JQ-1, SCH-1: 3 to 100 characters, dotted lowercase segments.</summary>
    public static bool IsValidName(string? name) =>
        name is { Length: >= MinNameLength and <= MaxNameLength } && NamePattern().IsMatch(name);

    /// <summary>JQ-1's check, also run by <c>AddEventHandler</c> on a subscriber name (spec 0013 OBX-5).</summary>
    internal static void EnsureValidName(string? name, string what)
    {
        if (!IsValidName(name))
        {
            throw new ConfigurationException(
                $"The {what} '{name}' is not valid: use {MinNameLength} to {MaxNameLength} characters of dotted lowercase "
                + "segments, such as jobs.cleanup (spec 0013 JQ-1, SCH-1).");
        }
    }

    /// <summary>JQ-5's option ranges, also run by <c>AddEventHandler</c> (spec 0013 OBX-5).</summary>
    internal static void EnsureValidOptions(string kind, JobKindOptions options)
    {
        if (options.Lease < JobKindOptions.MinLease || options.Lease > JobKindOptions.MaxLease)
        {
            throw new ConfigurationException($"The job kind {kind} has a Lease of {options.Lease}; use 1 minute to 1 hour (spec 0013 JQ-5).");
        }

        if (options.MaxAttempts is < 1 or > JobKindOptions.MaxMaxAttempts)
        {
            throw new ConfigurationException(
                $"The job kind {kind} has MaxAttempts {options.MaxAttempts}; use 1 to {JobKindOptions.MaxMaxAttempts} (spec 0013 JQ-5).");
        }

        if (options.Timeout < JobKindOptions.MinTimeout)
        {
            throw new ConfigurationException($"The job kind {kind} has a Timeout of {options.Timeout}; use at least 1 second (spec 0013 JQ-5).");
        }
    }

    // JQ-1's pattern, with \z for $ so a trailing newline does not match.
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
