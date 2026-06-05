using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.ScaleTesting;
using Marten.ScaleTesting.Domain;
using Marten.Storage;
using Microsoft.Extensions.Hosting;

// #4666 Phase A — host bootstrap. Mirrors the conjoined-multi-tenancy config
// from src/DaemonTests/Composites/multi_stage_projections.cs:246-254 but
// keeps the bucket count + tenancy style parametrisable per subcommand.
//
// Projections are registered at host build time so the Async daemon (Phase B)
// has them on hand; Phase A doesn't start the daemon, only seeds events
// against the same schema shape the rebuild will use.

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureServices(services =>
{
    services.AddMarten(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DisableNpgsqlLogging = true;

        // Quick append is the realistic shape for the high-throughput seed.
        opts.Events.AppendMode = EventAppendMode.Quick;

        // Conjoined tenancy with hash partitioning — bucket count is fixed
        // at 8 here so the schema is stable across seed/rebuild runs. The
        // CLI seed subcommand still threads its own bucket value but the
        // host-level config wins for partition DDL.
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x =>
        {
            x.ByHash(Enumerable.Range(1, 8).Select(i => $"b_{i}").ToArray());
        });
        opts.Advanced.DefaultTenantUsageEnabled = false;

        // Phase B — register the 4+2+2 composite projection per the #4666
        // issue's topology. Async lifecycle so the seed itself doesn't trigger
        // any projection work; rebuild is driven explicitly via the daemon
        // (the `rebuild` subcommand).
        //
        // Stage 1 — single-stream snapshots + the custom AppointmentMetrics
        //   IProjection (independent per-stream rollups).
        // Stage 2 — multi-stream + enrichment projections that read the
        //   stage-1 Updated<TUpstream> emissions (AppointmentDetails fans
        //   out from Updated<Appointment>+ProviderAssigned+AppointmentRouted;
        //   BoardSummary aggregates Updated<Board>+Updated<Appointment>+
        //   Updated<ProviderShift>).
        // Stage 3 — NEW projections that read stage-2 Updated<TDownstream>
        //   emissions, exercising the cross-stage chaining + per-tenant
        //   aggregation under the conjoined boundary.
        opts.Projections.CompositeProjectionFor("TelehealthComposite", projection =>
        {
            // Stage 1
            projection.Add<AppointmentProjection>(stageNumber: 1);
            projection.Add<ProviderShiftProjection>(stageNumber: 1);
            projection.Snapshot<Board>(stageNumber: 1);
            projection.Add(new AppointmentMetricsProjection(), stageNumber: 1);

            // Stage 2
            projection.Add<AppointmentDetailsProjection>(stageNumber: 2);
            projection.Add<BoardSummaryProjection>(stageNumber: 2);

            // Stage 3
            projection.Add<ProviderUtilizationProjection>(stageNumber: 3);
            projection.Add<TenantDailyRollupProjection>(stageNumber: 3);
        });
    });
});

return await builder.RunJasperFxCommands(args).ConfigureAwait(false);
