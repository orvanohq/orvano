using System.Text;

namespace Orvano.SharedKernel.Data;

/// <summary>The normal form of an email used for the indexed <c>*_normalized</c> column (spec 0011 COL-5).</summary>
public static class EmailAddress
{
    /// <summary>
    /// Trims surrounding white space, applies Unicode NFC, lowercases with the invariant culture, then applies NFC
    /// again (AC-9). It validates nothing and caps no length: the feature's validation checks the 254 limit.
    /// </summary>
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant().Normalize(NormalizationForm.FormC);
    }
}
