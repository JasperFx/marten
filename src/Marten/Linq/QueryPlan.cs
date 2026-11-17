#nullable enable
using System.Text.Json.Serialization;
using Npgsql;

namespace Marten.Linq;
// It seems that the output schema is not documented anywhere, so all the possible outputs
// should be deciphered from explain.c in PG backend codebase
// https://github.com/postgres/postgres/blob/18ce3a4ab22d2984f8540ab480979c851dae5338/src/backend/commands/explain.c

// 9.0: Deserialized directly with System.Text.Json via CommandRunnerExtensions
// (no longer routed through the user's ISerializer), so only the STJ
// JsonPropertyName attribute is needed. The Newtonsoft JsonProperty attributes
// that used to live here were dropped as part of the Marten.Newtonsoft package
// extraction so Marten core has no compile-time Newtonsoft.Json dependency.

public class QueryPlan
{
    /// <summary>
    ///     The scan type to be used to retrieve the data (ie sequential, index).
    /// </summary>
    [JsonPropertyName("Node Type")]
    public string NodeType { get; set; } = null!;

    /// <summary>
    ///     The table name from which the 'select' was queried.
    /// </summary>
    [JsonPropertyName("Relation Name")]
    public string RelationName { get; set; } = null!;

    /// <summary>
    ///     The table alias that was used (if none was used, <see cref="RelationName" /> is returned).
    /// </summary>
    public string Alias { get; set; } = null!;

    /// <summary>
    ///     The cost of initialising the query.
    ///     (note that "cost" does not have a unit - it's an arbitrary value)
    /// </summary>
    [JsonPropertyName("Startup Cost")]
    public decimal? StartupCost { get; set; }

    /// <summary>
    ///     The cost ofo performing the query.
    ///     (note that "cost" does not have a unit - it's an arbitrary value)
    /// </summary>
    [JsonPropertyName("Total Cost")]
    public decimal? TotalCost { get; set; }

    /// <summary>
    ///     The estimated number of rows returned.
    /// </summary>
    [JsonPropertyName("Plan Rows")]
    public int? PlanRows { get; set; }

    /// <summary>
    ///     The storage size of the query returned fields.
    /// </summary>
    [JsonPropertyName("Plan Width")]
    public int? PlanWidth { get; set; }

    [JsonPropertyName("Parallel Aware")]
    public bool ParallelAware { get; set; }

    /// <summary>
    ///     The index this node reads, on an Index Scan, Index Only Scan or Bitmap Index Scan. Null on other nodes.
    /// </summary>
    [JsonPropertyName("Index Name")]
    public string? IndexName { get; set; }

    /// <summary>
    ///     The condition the index is searched with, on an index scan node.
    /// </summary>
    [JsonPropertyName("Index Cond")]
    public string? IndexCondition { get; set; }

    /// <summary>
    ///     The condition a Bitmap Heap Scan checks again against each row it fetches.
    /// </summary>
    [JsonPropertyName("Recheck Cond")]
    public string? RecheckCondition { get; set; }

    /// <summary>
    ///     The condition applied to rows after they were read, e.g. a sequential scan's WHERE clause.
    /// </summary>
    [JsonPropertyName("Filter")]
    public string? Filter { get; set; }

    /// <summary>
    ///     How many rows <see cref="Filter" /> threw away. Only reported when the plan was explained with ANALYZE.
    /// </summary>
    [JsonPropertyName("Rows Removed by Filter")]
    public decimal? RowsRemovedByFilter { get; set; }

    /// <summary>
    ///     The direction an index scan walks the index: "Forward", "Backward" or "NoMovement".
    /// </summary>
    [JsonPropertyName("Scan Direction")]
    public string? ScanDirection { get; set; }

    [JsonPropertyName("Actual Startup Time")]
    public decimal? ActualStartupTime { get; set; }

    [JsonPropertyName("Actual Total Time")]
    public decimal? ActualTotalTime { get; set; }

    [JsonPropertyName("Actual Rows")]
    public decimal? ActualRows { get; set; }

    [JsonPropertyName("Actual Loops")]
    public decimal? ActualLoops { get; set; }

    [JsonPropertyName("Output")]
    public string[] Output { get; set; } = null!;

    [JsonPropertyName("Sort Key")]
    public string[] SortKey { get; set; } = null!;

    [JsonPropertyName("Sort Method")]
    public string SortMethod { get; set; } = null!;

    [JsonPropertyName("Sort Space Used")]
    public double SortSpaceUsed { get; set; }

    [JsonPropertyName("Sort Space Type")]
    public string SortSpaceType { get; set; } = null!;

    [JsonPropertyName("Plans")]
    public QueryPlan[] Plans { get; set; } = null!;

    // Lifted these from QueryPlanContainer so as not to change the returned type alltogether :|
    public decimal PlanningTime { get; set; }

    public decimal ExecutionTime { get; set; }

    /// <summary>
    ///     The command executed by Marten
    /// </summary>
    public NpgsqlCommand Command { get; set; } = null!;
}

internal class QueryPlanContainer
{
    public QueryPlan Plan { get; set; } = null!;

    [JsonPropertyName("Planning Time")]
    public decimal PlanningTime { get; set; }

    [JsonPropertyName("Execution Time")]
    public decimal ExecutionTime { get; set; }
}
