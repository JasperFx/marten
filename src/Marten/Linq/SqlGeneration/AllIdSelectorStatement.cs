using Marten.Internal;
using Marten.Linq.Fields;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class AllIdSelectorStatement: IdSelectorStatement
{
    public AllIdSelectorStatement(IMartenSession session, IFieldMapping fields, Statement parent): base(session, fields, parent)
    {
    }

    protected override void writeWhereClause(CommandBuilder sql)
    {
        if (Where is not ComparisonFilter comparisonFilter)
        {
            return;
        }

        sql.Append(" where ");
        Where = new AllComparisonFilter(comparisonFilter);
        Where.Apply(sql);
    }
}
