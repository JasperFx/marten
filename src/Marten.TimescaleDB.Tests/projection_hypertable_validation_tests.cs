using JasperFx.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Marten.TimescaleDB;
using Shouldly;
using Xunit;

namespace Marten.TimescaleDB.Tests;

// A flat-table projection keyed by the stream id (NOT the time column) — the shape that
// cannot become a hypertable, because TimescaleDB requires the partition column in the PK.
public class GuidKeyedMetricsProjection: FlatTableProjection
{
    public GuidKeyedMetricsProjection(): base("bad_metrics", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<DateTimeOffset>("captured_at").NotNull();
        Table.AddColumn<double>("value").NotNull();

        Project<SensorReadingRecorded>(map =>
        {
            map.Map(x => x.CapturedAt, "captured_at");
            map.Map(x => x.Value, "value");
        });
    }
}

[Collection("timescaledb")]
public class projection_hypertable_validation_tests
{
    [Fact]
    public async Task throws_a_helpful_error_when_time_column_is_not_the_primary_key()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "timescale_bad";
            opts.Events.DatabaseSchemaName = "timescale_bad";

            opts.Projections.Add(new GuidKeyedMetricsProjection(), ProjectionLifecycle.Inline);

            opts.UseTimescaleDB(ts =>
            {
                ts.ProjectionAsHypertable<GuidKeyedMetricsProjection>("captured_at");
            });
        });

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        ex.Message.ShouldContain("captured_at");
        ex.Message.ShouldContain("primary key");
    }
}
