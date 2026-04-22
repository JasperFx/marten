using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

/// <summary>
///     Reproducer attempt for https://github.com/JasperFx/marten/issues/4268.
///
///     The reporter changed three existing <c>Async</c> multi-stream projections
///     to <c>Inline</c> (also flipping <c>EnableSideEffectsOnInlineProjections = true</c>),
///     then hit a migration failure on the next event append:
///
///         DDL Execution for 'All Configured Changes' Failed!
///         alter table public.mt_doc_envelope drop constraint pkey_mt_doc_envelope_tenant_id_id CASCADE;
///         alter table public.mt_doc_envelope add CONSTRAINT pkey_mt_doc_envelope_id PRIMARY KEY (id);
///         alter table public.mt_doc_envelope drop column tenant_id;
///         ...
///         ---> 0A000: unique constraint on partitioned table must include all
///                     partitioning columns
///
///     Core mystery: the outgoing migration *drops* the tenant_id column and
///     replaces the composite pkey with a single-column pkey, which means Marten
///     believes the target table's tenancy style is Single — but the *existing*
///     table was built with Conjoined + partitioning, so the DROP fails.
///
///     This test runs the async → inline flip against a shared schema with
///     conjoined tenancy + archived-stream partitioning + envelope metadata
///     enabled on all documents (matching the reporter's CreateStoreOptions
///     helper) and expects the migration to succeed. If this test hangs or
///     throws, we have a reproducer. If it passes, we likely need more
///     info from the reporter.
/// </summary>
public class Bug_4268_projection_async_to_inline_migration : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4268_projection_async_to_inline_migration(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task switching_projection_from_async_to_inline_does_not_break_migration()
    {
        // 1) Build the schema with the projection registered as Async.
        StoreOptions(opts => ConfigureStore(opts, ProjectionLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // 2) Append an event and commit so the per-document tables (envelope
        //    doc, product doc) are materialized with the async-shaped DDL.
        var streamId = System.Guid.NewGuid();
        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<Bug4268Product>(streamId, new Bug4268Registered("Socks"));
            await session.SaveChangesAsync();
        }

        // 3) Spin up a SEPARATE store targeting the SAME schema, but with
        //    the projection flipped to Inline + side-effects-on-inline turned
        //    on. This mirrors the reporter's deploy: same DB, new code.
        var inline = SeparateStore(opts => ConfigureStore(opts, ProjectionLifecycle.Inline));

        // 4) Force the migration to run over the full model rather than
        //    just the document types that a single SaveChanges would
        //    materialize. This mirrors the widest possible surface the
        //    reporter's running app could hit on the first deploy and is
        //    where Marten's schema diff would emit the failing
        //    "drop partitioning column" DDL if the shape we hypothesized
        //    actually reproduces.
        await inline.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // 5) And a subsequent append still has to succeed.
        await using (var session = inline.LightweightSession("tenant-a"))
        {
            session.Events.StartStream<Bug4268Product>(
                System.Guid.NewGuid(), new Bug4268Registered("Hats"));

            // If the reporter's failure reproduces, this throws
            // Marten.Exceptions.MartenSchemaException wrapping
            // Npgsql.PostgresException 0A000.
            await session.SaveChangesAsync();
        }
    }

    private static void ConfigureStore(StoreOptions opts, ProjectionLifecycle lifecycle)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = "bug_4268";

        opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;

        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseIdentityMapForAggregates = true;
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseArchivedStreamPartitioning = true;
        opts.Events.EnableSideEffectsOnInlineProjections = true;
        opts.Events.MetadataConfig.EnableAll();

        opts.Advanced.DefaultTenantUsageEnabled = false;

        opts.Policies.AllDocumentsAreMultiTenanted();

        opts.Policies.ForAllDocuments(m =>
        {
            m.Metadata.CausationId.Enabled = true;
            m.Metadata.CorrelationId.Enabled = true;
            m.Metadata.Headers.Enabled = true;
            m.Metadata.Version.Enabled = true;
        });

        opts.Projections.Add<Bug4268ProductProjection>(lifecycle);
    }
}

public record Bug4268Registered(string Name);

public class Bug4268Product
{
    public System.Guid Id { get; set; }
    public string Name { get; set; } = "";

    public void Apply(Bug4268Registered e) => Name = e.Name;
}

public class Bug4268ProductProjection : SingleStreamProjection<Bug4268Product, System.Guid>
{
    public Bug4268ProductProjection()
    {
    }

    public static Bug4268Product Create(Bug4268Registered @event)
        => new() { Name = @event.Name };

    public void Apply(Bug4268Registered @event, Bug4268Product state)
        => state.Apply(@event);
}

public class Bug_4268_hash_partitioned_envelope_migration : IAsyncLifetime
{
    private const string SchemaName = "bug_4268_envelope";

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task changing_hash_partitioned_envelope_to_single_tenanted_should_not_fail()
    {
        await using (var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.DisableNpgsqlLogging = true;

            opts.Schema.For<Bug4268Envelope>()
                .MultiTenantedWithPartitioning(x => x.ByHash("h000", "h001"));
        }))
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        await using var singleTenanted = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.DisableNpgsqlLogging = true;

            opts.Schema.For<Bug4268Envelope>().SingleTenanted();
        });

        await singleTenanted.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}

[DocumentAlias("envelope")]
public class Bug4268Envelope
{
    public System.Guid Id { get; set; }
}
