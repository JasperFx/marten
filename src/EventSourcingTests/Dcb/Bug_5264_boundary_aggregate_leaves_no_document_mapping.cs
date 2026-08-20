#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// #5264: <c>EventGraph.Build&lt;TDoc&gt;()</c> resolved the aggregate's id type through
/// <c>StorageFeatures.MappingFor()</c> before it checked for <c>[BoundaryAggregate]</c>.
/// <c>MappingFor</c> is not a read-only probe — it registers a <c>DocumentMapping</c> — so a pure
/// boundary aggregate, which has no identity at all by design, became a registered "document type"
/// with no <c>IdMember</c> the first time it was fetched.
/// <para>
/// The fetch itself succeeded, which is what made this nasty: the damage only surfaced later, on
/// anything that enumerates <c>StorageFeatures.AllActiveFeatures</c> — <c>ResetAllData()</c>,
/// <c>ApplyAllConfiguredChangesToDatabaseAsync()</c>, <c>AssertDatabaseMatchesConfigurationAsync()</c>,
/// and the <c>db-patch</c> / <c>db-apply</c> commands — as
/// <c>InvalidDocumentException: Could not determine an 'id/Id' field or property</c>.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class Bug_5264_boundary_aggregate_leaves_no_document_mapping: OneOffConfigurationsContext, IAsyncLifetime
{
    public override ValueTask InitializeAsync()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<Enrolled>();
            opts.Events.AddEventType<ProgressRecorded>();

            opts.Events.RegisterTagType<EnrolleeId>("enrollee").ForAggregate<SubscriptionState>();
            opts.Events.RegisterTagType<ProgramId>("program").ForAggregate<SubscriptionState>();
        });

        return default;
    }

    public override ValueTask DisposeAsync() => base.DisposeAsync();

    private async Task FetchTheBoundaryAggregate()
    {
        await using var session = theStore.LightweightSession();
        await session.Events.FetchForWritingByTags<SubscriptionState>(
            new EventTagQuery().Or<EnrolleeId>(new EnrolleeId(Guid.NewGuid())));
    }

    [Fact]
    public async Task full_schema_operations_still_work_after_a_boundary_aggregate_fetch()
    {
        // One fetch is all it took to register the bogus mapping.
        await FetchTheBoundaryAggregate();

        // Each of these routes through DatabaseBase.AllObjects() -> AllActiveFeatures.
        await Should.NotThrowAsync(() => theStore.Advanced.ResetAllData());
        await Should.NotThrowAsync(() => theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        await Should.NotThrowAsync(() => theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task repeated_fetches_do_not_break_a_later_reset()
    {
        // The reported shape: a per-test ResetAllData() in an integration suite, where the first
        // test passes and every one after it fails.
        for (var i = 0; i < 3; i++)
        {
            await FetchTheBoundaryAggregate();
            await Should.NotThrowAsync(() => theStore.Advanced.ResetAllData());
        }
    }

    [Fact]
    public async Task no_document_table_is_generated_for_the_boundary_aggregate()
    {
        // Adding a dummy Id was the workaround, but it makes Marten emit a real mt_doc_* table for
        // an aggregate that is never stored — which then ships to every environment that generates
        // migration scripts. Nothing should be emitted for it at all.
        await FetchTheBoundaryAggregate();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var tables = await conn.ExistingTablesAsync(schemas: [SchemaName]);
        tables.Any(x => x.Name.Contains("subscriptionstate", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
    }
}
