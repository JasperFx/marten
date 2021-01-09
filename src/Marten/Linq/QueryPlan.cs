using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Npgsql;

namespace Marten.Linq
{
    // It seems that the output schema is not documented anywhere, so all the possible outputs
    // should be deciphered from explain.c in PG backend codebase
    // https://github.com/postgres/postgres/blob/18ce3a4ab22d2984f8540ab480979c851dae5338/src/backend/commands/explain.c

    public class QueryPlan
    {
        /// <summary>
        /// The scan type to be used to retrieve the data (ie sequential, index).
        /// </summary>
        [JsonPropertyName("Node Type")]
        [JsonProperty(PropertyName = "Node Type")]
        public string NodeType { get; set; }

        /// <summary>
        /// The table name from which the 'select' was queried.
        /// </summary>
        [JsonPropertyName("Relation Name")]
        [JsonProperty(PropertyName = "Relation Name")]
        public string RelationName { get; set; }

        /// <summary>
        /// The table alias that was used (if none was used, <see cref="RelationName"/> is returned).
        /// </summary>
        public string Alias { get; set; }

        /// <summary>
        /// The cost of initialising the query.
        /// (note that "cost" does not have a unit - it's an arbitrary value)
        /// </summary>
        [JsonPropertyName("Startup Cost")]
        [JsonProperty(PropertyName = "Startup Cost")]
        public decimal StartupCost { get; set; }

        /// <summary>
        /// The cost ofo performing the query.
        /// (note that "cost" does not have a unit - it's an arbitrary value)
        /// </summary>
        [JsonPropertyName("Total Cost")]
        [JsonProperty(PropertyName = "Total Cost")]
        public decimal TotalCost { get; set; }

        /// <summary>
        /// The estimated number of rows returned.
        /// </summary>
        [JsonPropertyName("Plan Rows")]
        [JsonProperty(PropertyName = "Plan Rows")]
        public int PlanRows { get; set; }

        /// <summary>
        /// The storage size of the query returned fields.
        /// </summary>
        [JsonPropertyName("Plan Width")]
        [JsonProperty(PropertyName = "Plan Width")]
        public int PlanWidth { get; set; }

        [JsonPropertyName("Parallel Aware")]
        [JsonProperty(PropertyName = "Parallel Aware")]
        public bool ParallelAware { get; set; }

        [JsonPropertyName("Actual Startup Time")]
        [JsonProperty(PropertyName = "Actual Startup Time")]
        public decimal ActualStartupTime { get; set; }

        [JsonPropertyName("Actual Total Time")]
        [JsonProperty(PropertyName = "Actual Total Time")]
        public decimal ActualTotalTime { get; set; }

        [JsonPropertyName("Actual Rows")]
        [JsonProperty(PropertyName = "Actual Rows")]
        public int ActualRows { get; set; }

        [JsonPropertyName("Actual Loops")]
        [JsonProperty(PropertyName = "Actual Loops")]
        public int ActualLoops { get; set; }

        [JsonPropertyName("Output")]
        [JsonProperty(PropertyName = "Output")]
        public string[] Output { get; set; }

        [JsonPropertyName("Sort Key")]
        [JsonProperty(PropertyName = "Sort Key")]
        public string[] SortKey { get; set; }

        [JsonPropertyName("Sort Method")]
        [JsonProperty(PropertyName = "Sort Method")]
        public string SortMethod { get; set; }

        [JsonPropertyName("Sort Space Used")]
        [JsonProperty(PropertyName = "Sort Space Used")]
        public double SortSpaceUsed { get; set; }

        [JsonPropertyName("Sort Space Type")]
        [JsonProperty(PropertyName = "Sort Space Type")]
        public string SortSpaceType { get; set; }

        [JsonPropertyName("Plans")]
        [JsonProperty(PropertyName = "Plans")]
        public QueryPlan[] Plans { get; set; }

        // Lifted these from QueryPlanContainer so as not to change the returned type alltogether :|
        public decimal PlanningTime { get; set; }

        public decimal ExecutionTime { get; set; }

        /// <summary>
        /// The command executed by Marten
        /// </summary>
        public NpgsqlCommand Command { get; set; }
    }

    internal class QueryPlanContainer
    {
        public QueryPlan Plan { get; set; }

        [JsonPropertyName("Planning Time")]
        [JsonProperty(PropertyName = "Planning Time")]
        public decimal PlanningTime { get; set; }

        [JsonPropertyName("Execution Time")]
        [JsonProperty(PropertyName = "Execution Time")]
        public decimal ExecutionTime { get; set; }
    }
}
