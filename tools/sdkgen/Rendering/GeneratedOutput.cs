namespace Orvano.SdkGen.Rendering;

internal enum Formatter
{
    Prettier,
    Dart,
    CSharp,
}

internal sealed record GeneratedFile(string Path, string Content);

/// <summary>
/// One generated area. The generator owns <see cref="Directory"/> completely: files it did not
/// write this run are deleted, so removing an operation removes its code.
/// </summary>
internal sealed record GeneratedOutput(string Name, string Directory, Formatter Formatter, IReadOnlyList<GeneratedFile> Files);
