namespace Marten.Linq.SqlGeneration
{
    internal class ScalarSelectManyStringStatement: SelectorStatement
    {
        public ScalarSelectManyStringStatement(SelectorStatement parent) : base(new ScalarStringSelectClause("data", parent.ExportName), null)
        {
        }
    }
}
