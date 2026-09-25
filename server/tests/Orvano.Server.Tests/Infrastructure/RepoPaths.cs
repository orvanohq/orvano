namespace Orvano.Server.Tests.Infrastructure;

public static class RepoPaths
{
    /// <summary>The repository root: the nearest folder above the test binaries that holds Orvano.slnx.</summary>
    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>The build configuration these tests were built with (Debug or Release).</summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // bin/<Configuration>/net10.0/

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orvano.slnx"))) return dir.FullName;
        }

        throw new InvalidOperationException("Could not find Orvano.slnx above " + AppContext.BaseDirectory);
    }
}
