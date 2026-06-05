using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.ScaleTesting;
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

        // Phase A intentionally does NOT register the snapshot projections —
        // the seeder only writes raw events. Registering Snapshot<T> would
        // require the JasperFx.Events.SourceGenerator to emit the dispatcher
        // for each aggregate's partial class, which is fine when we wire up
        // the composite projection in Phase B but is dead weight for a
        // pure-seeding run. Without registration, StartStream<T> on the
        // seeder just tags the stream with the aggregate type name; that
        // doesn't trigger any projection machinery and Phase B's rebuild
        // builds the snapshots from the seeded events.
    });
});

return await builder.RunJasperFxCommands(args).ConfigureAwait(false);
