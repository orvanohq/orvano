using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Orvano.Host.ConsoleFiles;

/// <summary>
/// A <see cref="PhysicalFileProvider"/> over the console folder that tolerates the folder not existing yet.
/// <see cref="PhysicalFileProvider"/> refuses a missing root, and a contributor may build the console while
/// the host runs; this creates the inner provider the first time the folder is there.
/// </summary>
internal sealed class ConsoleRootFileProvider(string root) : IFileProvider, IDisposable
{
    private readonly Lock _gate = new();
    private PhysicalFileProvider? _inner;

    public string Root { get; } = root;

    public IFileInfo GetFileInfo(string subpath) =>
        Inner is { } inner ? inner.GetFileInfo(subpath) : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) =>
        Inner is { } inner ? inner.GetDirectoryContents(subpath) : NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    public void Dispose() => _inner?.Dispose();

    private PhysicalFileProvider? Inner
    {
        get
        {
            if (_inner is not null)
            {
                return _inner;
            }

            if (!Directory.Exists(Root))
            {
                return null;
            }

            lock (_gate)
            {
                // Hidden and system files stay excluded, the PhysicalFileProvider default.
                _inner ??= new PhysicalFileProvider(Root);
                return _inner;
            }
        }
    }
}
