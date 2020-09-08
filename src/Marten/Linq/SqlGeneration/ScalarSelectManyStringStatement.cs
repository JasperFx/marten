namespace Marten.Linq.SqlGeneration
{
    public class ScalarSelectManyStringStatement: SelectorStatement
    {
        public ScalarSelectManyStringStatement(SelectorStatement parent) : base(new ScalarStringSelectClause("data", parent.ExportName), null)
        {
        }
    }
}
