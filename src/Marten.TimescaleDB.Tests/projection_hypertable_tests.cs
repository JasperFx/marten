using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Marten.TimescaleDB;
using Shouldly;
using Xunit;

namespace Marten.TimescaleDB.Tests;

[Collection("timescaledb")]
public class projection_hypertable_tests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "timescale_proj";
            opts.Events.DatabaseSchemaName = "timescale_proj";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.Projections.Add(new MetricsProjection(), ProjectionLifecycle.Inline);

            opts.UseTimescaleDB(ts =>
            {
                ts.ProjectionAsHypertable<MetricsProjection>("captured_at", hyper =>
                {
                    hyper.ChunkInterval = TimeSpan.FromHours(1);
                    hyper.CompressAfter = TimeSpan.FromDays(30);
                    hyper.RetainFor = TimeSpan.FromDays(365);
                    hyper.ContinuousAggregate("hourly_metrics", "1 hour",
                        "avg(value) as avg_val, max(value) as max_val");
                });
            });
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    [Fact]
    public async Task timescaledb_extension_is_created()
    {
        var count = await ScalarAsync<long>("select count(*) from pg_extension where extname = 'timescaledb'");
        count.ShouldBe(1);
    }

    [Fact]
    public async Task projection_table_is_a_hypertable()
    {
        var count = await ScalarAsync<long>(
            "select count(*) from timescaledb_information.hypertables where hypertable_schema = 'timescale_proj' and hypertable_name = 'sensor_metrics'");
        count.ShouldBe(1);
    }

    [Fact]
    public async Task compression_and_retention_policies_are_registered()
    {
        var jobs = await ScalarAsync<long>(
            "select count(*) from timescaledb_information.jobs where hypertable_schema = 'timescale_proj' and hypertable_name = 'sensor_metrics'");
        // one compression policy + one retention policy
        jobs.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task continuous_aggregate_is_created()
    {
        var count = await ScalarAsync<long>(
            "select count(*) from timescaledb_information.continuous_aggregates where view_schema = 'timescale_proj' and view_name = 'hourly_metrics'");
        count.ShouldBe(1);
    }

    [Fact]
    public async Task reapplying_changes_is_idempotent()
    {
        // Applying a second time must not error — the hypertable / continuous-aggregate
        // schema objects detect that they already exist and produce no delta.
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task database_matches_configuration_after_conversion()
    {
        // Proves the hypertable conversion does not leave Marten's own schema-diff seeing drift
        // (which would otherwise try to "fix" the table on every migration).
        await Should.NotThrowAsync(async () =>
            await _store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task projection_writes_land_in_the_hypertable()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var session = _store.LightweightSession())
        {
            for (var i = 0; i < 24; i++)
            {
                session.Events.StartStream(Guid.NewGuid(),
                    new SensorReadingRecorded(Guid.NewGuid(), baseTime.AddHours(i), 10 + i));
            }

            await session.SaveChangesAsync();
        }

        var rows = await ScalarAsync<long>("select count(*) from timescale_proj.sensor_metrics");
        rows.ShouldBe(24);

        var maxValue = await ScalarAsync<double>("select max(value) from timescale_proj.sensor_metrics");
        maxValue.ShouldBe(33);
    }
}
