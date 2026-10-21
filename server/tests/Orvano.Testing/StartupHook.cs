using System.Diagnostics;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Loaded into the real <c>orvano</c> process through <see cref="Orvano.Testing.OrvanoCli.UndeclaredEndpointHook"/>:
/// maps <c>/test/undeclared</c>, an endpoint that declares no access, so a test can see what <c>serve</c> prints when
/// the startup check (spec 0012 EPT-4) refuses it. With <see cref="Orvano.Testing.OrvanoCli.JobsHookVariable"/> set, it
/// registers the bad job kind or schedule that variable names instead (spec 0013 SCH-1), and with
/// <see cref="Orvano.Testing.OrvanoCli.OutboxHookVariable"/> set, the bad outbox registration it names (OBX-7). The runtime finds the hook by
/// this name, outside any namespace.
/// </summary>
#pragma warning disable CA1050 // DOTNET_STARTUP_HOOKS looks for a type named StartupHook in no namespace
internal static class StartupHook
#pragma warning restore CA1050
{
    public static void Initialize() => DiagnosticListener.AllListeners.Subscribe(new Listeners());

    private sealed class Listeners : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.Extensions.Hosting")
            {
                value.Subscribe(new HostBuilding());
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class HostBuilding : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            // The same hook WebApplicationFactory uses: the builder, just before it builds the host.
            if (value is { Key: "HostBuilding", Value: IHostBuilder builder })
            {
                builder.ConfigureServices(services =>
                {
                    if (Environment.GetEnvironmentVariable(Orvano.Testing.OrvanoCli.JobsHookVariable) is { Length: > 0 } jobs)
                    {
                        Orvano.Testing.BadJobRegistrations.Add(services, jobs);
                    }
                    else if (Environment.GetEnvironmentVariable(Orvano.Testing.OrvanoCli.OutboxHookVariable) is { Length: > 0 } outbox)
                    {
                        Orvano.Testing.BadOutboxRegistrations.Add(services, outbox);
                    }
                    else
                    {
                        services.AddTransient<IStartupFilter, UndeclaredEndpoint>();
                    }
                });
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    // Maps on the host's own route builder (the WebApplication, handed on in the builder's properties).
    private sealed class UndeclaredEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Properties.Values.OfType<IEndpointRouteBuilder>().Single().MapGet("/test/undeclared", () => "ran");
        };
    }
}
