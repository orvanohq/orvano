using Microsoft.EntityFrameworkCore.Storage;

namespace Orvano.SharedKernel.Data;

/// <summary>Maps <see cref="EncryptedValue"/> to <c>bytea</c>, passing the bytes through (spec 0011 ENC-1).</summary>
internal sealed class EncryptedValueTypeMapping : RelationalTypeMapping
{
    public static readonly EncryptedValueTypeMapping Instance = new();

    private EncryptedValueTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(EncryptedValue), new EncryptedValue.Converter(), new EncryptedValue.Comparer()),
            "bytea",
            StoreTypePostfix.None,
            System.Data.DbType.Binary))
    {
    }

    private EncryptedValueTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new EncryptedValueTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value) =>
        $"'\\x{Convert.ToHexString((byte[])value)}'::bytea";
}

/// <summary>Lets EF discover <see cref="EncryptedValue"/> properties as <c>bytea</c> columns.</summary>
internal sealed class EncryptedValueTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo) =>
        mappingInfo.ClrType == typeof(EncryptedValue) ? EncryptedValueTypeMapping.Instance : null;
}
