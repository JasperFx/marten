using Newtonsoft.Json;

namespace Marten.Linq
{
    public class QueryPlan
    {
        /// <summary>
        /// The scan type to be used to retrieve the data (ie sequential, index).
        /// </summary>
        [JsonProperty(PropertyName = "Node Type")]
        public string NodeType { get; set; }
        
        /// <summary>
        /// The table name from which the 'select' was queried.
        /// </summary>
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
        [JsonProperty(PropertyName = "Startup Cost")]
        public decimal StartupCost { get; set; }

        /// <summary>
        /// The cost ofo performing the query.
        /// (note that "cost" does not have a unit - it's an arbitrary value)
        /// </summary>
        [JsonProperty(PropertyName = "Total Cost")]
        public decimal TotalCost { get; set; }

        /// <summary>
        /// The estimated number of rows returned.
        /// </summary>
        [JsonProperty(PropertyName = "Plan Rows")]
        public int PlanRows { get; set; }

        /// <summary>
        /// The storage size of the query returned fields.
        /// </summary>
        [JsonProperty(PropertyName = "Plan Width")]
        public int PlanWidth { get; set; }
    }

    class QueryPlanContainer
    {
        public QueryPlan Plan { get; set; }
    }
}