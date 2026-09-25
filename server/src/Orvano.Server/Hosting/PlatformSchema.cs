using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

/// <summary>The platform migrations embedded in this binary, and the schema version it expects.</summary>
public static class PlatformSchema
{
    public static IReadOnlyList<PlatformMigration> Migrations { get; } =
        PlatformMigration.LoadEmbedded(typeof(PlatformSchema).Assembly);

    public static int ExpectedVersion => Migrations.Count == 0 ? 0 : Migrations[^1].Version;
}
