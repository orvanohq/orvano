namespace Orvano.SharedKernel;

/// <summary>
/// The first event ID of each module's block of 1000 (spec 0002 LOG-1). New modules take the next free block.
/// </summary>
public static class LogEventIds
{
    public const int Host = 0;
    public const int Auth = 1000;
    public const int Databases = 2000;
    public const int Storage = 3000;
    public const int Realtime = 4000;
    public const int Functions = 5000;
    public const int Sites = 6000;
    public const int Messaging = 7000;
    public const int Operations = 8000;
    public const int Platform = 9000;
    public const int Infrastructure = 10000;
}
