namespace Marten.Internal.Linq
{
    public class ScalarSelectManyStringStatement: Statement
    {
        public ScalarSelectManyStringStatement(Statement parent) : base(new ScalarStringSelectClause("data", parent.ExportName), null)
        {
        }
    }
}
