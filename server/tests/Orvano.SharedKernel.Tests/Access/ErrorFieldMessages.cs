namespace Orvano.SharedKernel.Tests.Access;

internal static class ErrorFieldMessages
{
    /// <summary>The messages of the error's field errors on <paramref name="field"/>, in order.</summary>
    public static string[] FieldMessages(this Error error, string field) =>
        [.. (error.Fields ?? []).Where(e => e.Field == field).Select(e => e.Message)];
}
