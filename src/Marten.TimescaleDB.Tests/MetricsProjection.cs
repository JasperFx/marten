using Marten.Events.Projections.Flattened;

namespace Marten.TimescaleDB.Tests;

public record SensorReadingRecorded(Guid SensorId, DateTimeOffset CapturedAt, double Value);

/// <summary>
/// A time-bucketed flat-table projection whose single primary key IS the time column.
/// This is the shape that maps cleanly onto a TimescaleDB hypertable: the partition column
/// participates in the (only) unique key, so create_hypertable() and the upsert's
/// ON CONFLICT (captured_at) both agree on the same column.
/// </summary>
public class MetricsProjection: FlatTableProjection
{
    public MetricsProjection(): base("sensor_metrics", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<DateTimeOffset>("captured_at").AsPrimaryKey();
        Table.AddColumn<double>("value").NotNull();

        Project<SensorReadingRecorded>(map =>
        {
            map.Map(x => x.Value, "value");
        }, tablePrimaryKeySource: x => x.CapturedAt);
    }
}
