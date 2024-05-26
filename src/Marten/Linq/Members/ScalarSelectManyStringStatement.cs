#nullable enable
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Members;

internal class ScalarSelectManyStringStatement: SelectorStatement
{
    public ScalarSelectManyStringStatement(SelectorStatement parent)
    {
        SelectClause = new ScalarStringSelectClause("data", parent.ExportName);
    }
}
