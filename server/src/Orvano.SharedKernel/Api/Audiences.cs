namespace Orvano.SharedKernel.Api;

/// <summary>The <c>x-orvano-audience</c> values (spec 0014 XTN-1): which SDKs an operation is generated into (GNR-4).</summary>
public static class Audiences
{
    /// <summary>Client SDKs: guests and end users with a publishable key.</summary>
    public const string Client = "client";

    /// <summary>Server SDKs: secret and run keys.</summary>
    public const string Server = "server";
}
