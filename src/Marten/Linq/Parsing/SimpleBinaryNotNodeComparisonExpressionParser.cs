namespace Marten.Linq.Parsing
{
    public class SimpleBinaryNotNodeComparisonExpressionParser: SimpleBinaryComparisonExpressionParser
    {
        public SimpleBinaryNotNodeComparisonExpressionParser() : base("is not", "not ")
        {
        }
    }
}
