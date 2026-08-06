using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests.Projections.Flattened;

// The insert / update / increment / decrement / delete behavior this file used to assert now lives
// in JasperFx.Events.ComplianceTests.FlatTableProjectionCompliance, enrolled in Compliance/. What is
// left here is deliberately PostgreSQL-specific and out of compliance scope: the generated DDL and
// the mt_upsert_* functions Marten writes through, plus a codegen smoke test.
public class flat_table_projection_with_stream_id_identifier_end_to_end: OneOffConfigurationsContext
{
    public flat_table_projection_with_stream_id_identifier_end_to_end()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<WriteTableWithGuidIdentifierProjection>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task table_should_be_built()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var table = await new Table(new PostgresqlObjectName(SchemaName, "values")).FetchExistingAsync(conn);

        table.PrimaryKeyColumns.Single().ShouldBe("id");
        table.Columns.Select(x => x.Name).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("a", "b", "c", "d", "id", "revision", "status");
    }

    [Fact]
    public async Task functions_are_built()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var functions = await theStore.Storage.Database.Functions();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuesadded").ShouldBeTrue();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuesset").ShouldBeTrue();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuessubtracted").ShouldBeTrue();

        functions.Any().ShouldBeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task try_compilation()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<SiteProjection>(ProjectionLifecycle.Inline);
        });

        var id = theSession.Events.StartStream(new SiteCreated("one")).Id;
        await theSession.SaveChangesAsync();
    }
}

public record SiteCreated(string Name);

public record SiteEnrolledToLite();

public record SiteLocationRecorded(decimal Latitude, decimal Longitude);

public class SiteProjection : FlatTableProjection
{
    public SiteProjection()
        : base("site_projection", SchemaNameSource.DocumentSchema)
    {
        _ = Table.AddColumn<Guid>("id").AsPrimaryKey();

        Options.TeardownDataOnRebuild = true;

        Project<SiteCreated>(map =>
        {
            _ = map.Map(x => x.Name);

            _ = map.SetValue("is_lite", 0);
            _ = map.SetValue("created_at", DateTimeOffset.UtcNow.ToString());
        });

        Project<SiteEnrolledToLite>(map =>
        {
            map.SetValue("is_lite", 0);
        });

        Project<SiteLocationRecorded>(map =>
        {
            _ = map.Map(x => x.Latitude);
            _ = map.Map(x => x.Longitude);
        });
    }
}

