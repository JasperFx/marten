using Newtonsoft.Json;

namespace Marten.Linq
{
    public class QueryPlan
    {
        [JsonProperty(PropertyName = "Node Type")]
        public string NodeType { get; set; }
        [JsonProperty(PropertyName = "Relation Name")]
        public string RelationName { get; set; }
        public string Alias { get; set; }
        [JsonProperty(PropertyName = "Startup Cost")]
        public decimal StartupCost { get; set; }
        [JsonProperty(PropertyName = "Total Cost")]
        public decimal TotalCost { get; set; }
        [JsonProperty(PropertyName = "Plan Rows")]
        public int PlanRows { get; set; }
        [JsonProperty(PropertyName = "Plan Width")]
        public int PlanWidth { get; set; }
    }

    class QueryPlanContainer
    {
        public QueryPlan Plan { get; set; }
    }
}