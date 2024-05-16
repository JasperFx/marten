namespace Marten.Testing.Examples;

public class enabling_npgsql_tracing
{
    public static void bootstrap_with_npgsql_logging()
    {
        #region sample_enabling_npgsql_tracing

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection");

            // Unleash the hounds!
            opts.DisableNpgsqlLogging = false;
        });

        #endregion
    }
}
