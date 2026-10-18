using System.Text.Json;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Npgsql;

using Orvano.SharedKernel.Data;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Data;

// Spec 0011 ENC-1: the stored envelope compares by its bytes and never prints them (DataClass.Secret).
public sealed class EncryptedValueTests
{
    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    public void ToString_shows_only_the_length_when_printed()
    {
        var value = new EncryptedValue(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        value.ToString().ShouldBe("EncryptedValue(4 bytes)");
        $"{value}".ShouldNotContain("DE");
    }

    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    public void Equals_compares_the_bytes_when_the_arrays_differ()
    {
        var left = new EncryptedValue(new byte[] { 1, 2, 3 });
        var right = new EncryptedValue(new byte[] { 1, 2, 3 });

        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
        left.ShouldNotBe(new EncryptedValue(new byte[] { 1, 2, 4 }));
        left.ShouldNotBe(new EncryptedValue(new byte[] { 1, 2 }));
    }

    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    [Trait("Spec", "0006:LOG-3")]
    public void Json_shows_only_the_length_when_an_entity_is_serialized()
    {
        var widget = new Widget { Secret = new EncryptedValue(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }) };

        string json = JsonSerializer.Serialize(widget);

        json.ShouldContain("\"Secret\":\"EncryptedValue(4 bytes)\"");
        json.ShouldNotContain(Convert.ToBase64String([0xDE, 0xAD, 0xBE, 0xEF]));
    }

    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    public void Json_read_throws_when_given_an_envelope() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EncryptedValue>("\"3q2+7w==\""));

    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    public async Task Change_is_detected_when_the_bytes_are_changed_in_place()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
        await using var context = SampleContext.Create(dataSource, new TestClock(DateTimeOffset.UnixEpoch));
        byte[] bytes = [1, 2, 3];
        var widget = new Widget { Secret = new EncryptedValue(bytes) };
        context.Attach(widget);

        bytes[0] = 9;
        context.ChangeTracker.DetectChanges();

        PropertyEntry<Widget, EncryptedValue?> secret = context.Entry(widget).Property(x => x.Secret);
        secret.IsModified.ShouldBeTrue();
        secret.OriginalValue.ShouldBe(new EncryptedValue(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    [Trait("Spec", "0011:ENC-1")]
    public async Task Storing_throws_when_the_envelope_is_empty()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused");
        await using var context = SampleContext.Create(dataSource, new TestClock(DateTimeOffset.UnixEpoch));
        ValueConverter converter = context.Model.FindEntityType(typeof(Widget))!.FindProperty(nameof(Widget.Secret))!.GetTypeMapping().Converter!;

        Should.Throw<InvalidOperationException>(() => converter.ConvertToProvider(default(EncryptedValue))).Message.ShouldContain("is empty");
        converter.ConvertToProvider(new EncryptedValue(new byte[] { 1 })).ShouldBe(new byte[] { 1 });
    }
}
