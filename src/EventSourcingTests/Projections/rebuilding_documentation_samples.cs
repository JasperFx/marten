using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Events.Aggregation;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Projections.RebuildingSamples;

#region sample_rebuilding_gate_side_effects_behind_prior_version

public class TripProjection : SingleStreamProjection<Trip, Guid>
{
    public TripProjection()
    {
        Version = 3;

        // On by default: side effects only fire for events the prior version (V2) never processed.
        // Set to false to opt out and let the new version re-emit them all
        GateSideEffectsBehindPriorVersion = false;
    }

    // Apply(...) methods and RaiseSideEffects(...) as usual
}

#endregion

/// <summary>
///     The code samples in docs/events/projections/rebuilding.md that no other test shows. Compiled only, never run.
/// </summary>
public static class rebuilding_documentation_samples
{
    public static void rebuild_with_bulk_copy()
    {
        var builder = Host.CreateApplicationBuilder();

        #region sample_rebuilding_with_bulk_copy

        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // When a rebuild batch's document writes are pure inserts, flush them
            // through PostgreSQL binary COPY instead of the per-row INSERT path.
            opts.Projections.RebuildWithBulkCopy = true;
        });

        #endregion
    }

    public static async Task rebuild_one_identity(IProjectionDaemon daemon, Guid tripId, CancellationToken token)
    {
        #region sample_rebuilding_one_identity_through_the_daemon

        var result = await daemon.RebuildIdentityAsync("Trip", tenantId: null, identity: tripId, token);

        // The document is now consistent with every event of its stream up to result.BoundCeiling

        #endregion
    }
}
