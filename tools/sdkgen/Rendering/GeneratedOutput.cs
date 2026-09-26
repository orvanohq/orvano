namespace Orvano.SdkGen.Rendering;

internal enum Formatter
{
    /// <summary>Written as rendered: the templates are already formatted.</summary>
    None,
    Prettier,
    Dart,
    CSharp,
}

internal sealed record GeneratedFile(string Path, string Content);

/// <summary>
/// One generated area. The generator owns <see cref="Directory"/> completely: files it did not
/// write this run are deleted, so removing an operation removes its code. When
/// <see cref="OwnsDirectory"/> is false (a file beside hand made ones), only its files are written.
/// </summary>
internal sealed record GeneratedOutput(string Name, string Directory, Formatter Formatter, IReadOnlyList<GeneratedFile> Files, bool OwnsDirectory = true);
