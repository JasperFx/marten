namespace Marten.ScaleTesting;

/// <summary>
/// Mirrors <c>DaemonTests.TeleHealth.ConnectionSource</c> but copy-pasted so the
/// dev-tool stays self-contained (no <c>ProjectReference</c> back into
/// DaemonTests). Override at runtime via the <c>marten_testing_database</c>
/// environment variable, same convention as the test harness.
/// </summary>
internal static class ConnectionSource
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("marten_testing_database") ?? DefaultConnectionString;
}
