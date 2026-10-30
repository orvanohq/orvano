namespace Orvano.Modules.Platform.Setup;

/// <summary>
/// Where the setup banner goes: standard output through <see cref="System.Console.Out"/>, never <c>ILogger</c>, so the token
/// never reaches OpenTelemetry export (spec 0016 STP-4, spec 0006 CON-6's exception to SCR-5). Tests swap the writer.
/// </summary>
internal sealed class SetupOutput(TextWriter writer)
{
    public static SetupOutput Standard { get; } = new(System.Console.Out);

    /// <summary>
    /// STP-4: a blank line, the link, the expiry with the command for a new link, and a blank line, in one write so log
    /// lines cannot land in between.
    /// </summary>
    public void WriteBanner(string link, DateTimeOffset expiresAt) => Write(
        $"{Environment.NewLine}Finish setting up Orvano: {link}{Environment.NewLine}"
        + $"This link works until setup is done or {SetupToken.Format(expiresAt)}. Make a new one with: orvano admin setup-token{Environment.NewLine}"
        + Environment.NewLine);

    /// <summary>STP-4's short banner, when a valid token already exists.</summary>
    public void WriteWaiting() => Write($"Orvano setup is waiting. Make a setup link with: orvano admin setup-token{Environment.NewLine}");

    private void Write(string text)
    {
        writer.Write(text);
        writer.Flush();
    }
}
