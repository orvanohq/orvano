using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Orvano.Core.Migrations;

/// <summary>One versioned <c>NNNN_name.sql</c> file, embedded in the server assembly.</summary>
public sealed partial record PlatformMigration(int Version, string Name, string Sql, string Sha256)
{
    /// <summary>The logical name prefix of the embedded migration resources.</summary>
    public const string ResourcePrefix = "migrations/platform/";

    [GeneratedRegex(@"^(\d{4})_([a-z0-9_]+)\.sql\z")]
    private static partial Regex FileNamePattern();

    /// <summary>Loads every migration embedded in <paramref name="assembly"/>, sorted by version.</summary>
    /// <exception cref="InvalidOperationException">A file name does not match <c>NNNN_name.sql</c>, or two files share a version.</exception>
    public static IReadOnlyList<PlatformMigration> LoadEmbedded(Assembly assembly)
    {
        var migrations = new List<PlatformMigration>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var fileName = resource[ResourcePrefix.Length..];
            var match = FileNamePattern().Match(fileName);
            if (!match.Success)
                throw new InvalidOperationException($"Migration file '{fileName}' does not match NNNN_<name>.sql.");

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Hash LF text so a Windows checkout with CRLF endings still matches the applied checksum.
            var sql = reader.ReadToEnd().Replace("\r\n", "\n");
            var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            migrations.Add(new PlatformMigration(int.Parse(match.Groups[1].Value), match.Groups[2].Value, sql, sha));
        }

        var duplicate = migrations.GroupBy(m => m.Version).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Two migration files share version {duplicate.Key:D4}.");

        return migrations.OrderBy(m => m.Version).ToList();
    }
}
