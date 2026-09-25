namespace Orvano.Server.Hosting;

internal static class OrvanoEnvironments
{
    /// <summary>
    /// The environment the shared scenarios and integration tests run in. It turns on contract
    /// validation and allows <c>ORVANO_TEST_FIXTURES</c> (spec 0001).
    /// </summary>
    public const string Test = "Test";
}
