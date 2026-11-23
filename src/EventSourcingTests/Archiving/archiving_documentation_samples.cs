using Marten;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Archiving;

/// <summary>
///     The code samples in docs/events/archiving.md that no other test shows. Compiled only, never run.
/// </summary>
public static class archiving_documentation_samples
{
    public static void strict_stream_identity_with_partitioning()
    {
        #region sample_archiving_strict_stream_identity_with_partitioning

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Recommended companion when stream ids come from outside Marten
            // (especially string keys) and you also want hot/cold partitioning.
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
        });

        #endregion
    }
}
