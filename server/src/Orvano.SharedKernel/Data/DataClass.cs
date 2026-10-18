namespace Orvano.SharedKernel.Data;

/// <summary>
/// How sensitive a stored value is (spec 0006 LOG-2, spec 0011 CLS-1). One value per property, not flags.
/// </summary>
public enum DataClass
{
    /// <summary>Must never be logged or shown back: an <see cref="EncryptedValue"/>, a hash, an HMAC.</summary>
    Secret,

    /// <summary>About a person: an email, a name, an IP address, a user agent. Feature 49 finds these.</summary>
    Personal,

    /// <summary>Neither secret nor personal.</summary>
    Public,
}

/// <summary>
/// Marks the data class of an entity property that carries text, bytes, an address, or JSON (spec 0011 CLS-1, AC-7).
/// An <see cref="EncryptedValue"/> property is <see cref="DataClass.Secret"/> with a fixed <see cref="Purpose"/>
/// (ENC-2), for example <c>[DataClass(DataClass.Secret, Purpose = "auth.signing_key.private")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class DataClassAttribute(DataClass dataClass) : Attribute
{
    public DataClass DataClass { get; } = dataClass;

    /// <summary>
    /// The purpose bound into an <see cref="EncryptedValue"/>'s authenticated data. A constant set once in code, never
    /// derived from table or column names, so a rename does not break stored secrets (ENC-2).
    /// </summary>
    public string? Purpose { get; init; }
}
