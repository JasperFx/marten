using System;
using System.Collections.Generic;
namespace Marten.TimescaleDB;

/// <summary>
/// Describes a TimescaleDB continuous aggregate (a materialized view that TimescaleDB
/// keeps refreshed as new data lands in the underlying hypertable).
/// </summary>
public sealed class ContinuousAggregateDefinition
{
    public ContinuousAggregateDefinition(string viewName, string bucketInterval, string selectExpressions)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            throw new ArgumentException("A continuous aggregate view name is required", nameof(viewName));
        if (string.IsNullOrWhiteSpace(bucketInterval))
            throw new ArgumentException("A time_bucket interval is required", nameof(bucketInterval));
        if (string.IsNullOrWhiteSpace(selectExpressions))
            throw new ArgumentException("At least one aggregate select expression is required", nameof(selectExpressions));

        ViewName = viewName;
        BucketInterval = bucketInterval;
        SelectExpressions = selectExpressions;
    }

    /// <summary>
    /// The (unqualified) name of the materialized view. It is created in the same schema as the hypertable.
    /// </summary>
    public string ViewName { get; }

    /// <summary>
    /// The time_bucket() width, e.g. "1 hour" or "5 minutes".
    /// </summary>
    public string BucketInterval { get; }

    /// <summary>
    /// The raw SQL aggregate expressions selected alongside the time bucket, e.g.
    /// "avg(value) as avg_val, max(value) as max_val".
    /// </summary>
    public string SelectExpressions { get; }

    /// <summary>
    /// Optional extra GROUP BY columns (besides the time bucket), e.g. "sensor_id".
    /// Any column referenced here must also appear in <see cref="SelectExpressions"/>.
    /// </summary>
    public string? GroupByColumns { get; set; }
}

/// <summary>
/// Configuration for turning a single Marten-managed table into a TimescaleDB hypertable.
/// </summary>
public class HypertableOptions
{
    public HypertableOptions(string timeColumn)
    {
        if (string.IsNullOrWhiteSpace(timeColumn))
            throw new ArgumentException("A time/partitioning column is required", nameof(timeColumn));

        TimeColumn = timeColumn;
    }

    /// <summary>
    /// The column TimescaleDB partitions ("chunks") the hypertable by. Must be a timestamp,
    /// timestamptz, date, or integer column and — for hypertables — must participate in every
    /// unique/primary key on the table.
    /// </summary>
    public string TimeColumn { get; }

    /// <summary>
    /// Width of each time chunk. Maps to create_hypertable(..., chunk_time_interval => ...).
    /// Defaults to TimescaleDB's own default (7 days) when null.
    /// </summary>
    public TimeSpan? ChunkInterval { get; set; }

    /// <summary>
    /// When set, enables native columnar compression and adds a compression policy that
    /// compresses chunks older than this age. Maps to add_compression_policy(...).
    /// </summary>
    public TimeSpan? CompressAfter { get; set; }

    /// <summary>
    /// Optional comma-separated list of columns used as the compression "segment by" key.
    /// Only meaningful when <see cref="CompressAfter"/> is set.
    /// </summary>
    public string? CompressSegmentBy { get; set; }

    /// <summary>
    /// Optional comma-separated list of columns used as the compression "order by" key.
    /// Only meaningful when <see cref="CompressAfter"/> is set. Defaults to the time column DESC.
    /// </summary>
    public string? CompressOrderBy { get; set; }

    /// <summary>
    /// When set, adds a retention policy that drops chunks older than this age.
    /// Maps to add_retention_policy(...).
    /// </summary>
    public TimeSpan? RetainFor { get; set; }

    internal List<ContinuousAggregateDefinition> ContinuousAggregates { get; } = new();

    /// <summary>
    /// Register a TimescaleDB continuous aggregate over this hypertable.
    /// </summary>
    /// <param name="viewName">Unqualified name of the materialized view.</param>
    /// <param name="bucketInterval">time_bucket() width, e.g. "1 hour".</param>
    /// <param name="selectExpressions">Raw SQL aggregate expressions, e.g. "avg(value) as avg_val, max(value) as max_val".</param>
    /// <param name="groupByColumns">Optional extra GROUP BY columns besides the time bucket, e.g. "sensor_id".</param>
    public HypertableOptions ContinuousAggregate(string viewName, string bucketInterval, string selectExpressions,
        string? groupByColumns = null)
    {
        ContinuousAggregates.Add(new ContinuousAggregateDefinition(viewName, bucketInterval, selectExpressions)
        {
            GroupByColumns = groupByColumns
        });
        return this;
    }
}
